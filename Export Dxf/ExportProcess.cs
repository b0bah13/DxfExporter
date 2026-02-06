using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Printing.IndexedProperties;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Xml.Linq;
using DxfExporter.Constants;
using DxfExporter.MaskProcess;
using DxfExporter.Scanning;
using Inventor;
using static System.Net.Mime.MediaTypeNames;
using static DxfExporter.MainWindow;
using Path = System.IO.Path;

namespace DxfExporter.Export_Dxf
{
    /// <summary>
    /// Класс-контекст, который передаётся по всей цепочке методов экспорта
    /// </summary>
    internal class ExportContext
    {
        /// <summary>
        /// Экземпляр приложения Inventor (COM-объект)
        /// </summary>
        public Inventor.Application InvApp { get; }

        /// <summary>
        /// Список деталей, которые нужно обработать
        /// </summary>
        public ObservableCollection<StructureClass> ProcData { get; }

        /// <summary>
        /// Маска выгрузки
        /// </summary>
        public ObservableCollection<MaskPart> MaskData { get; }

        /// <summary>
        /// Режим пользовательских настроек
        /// </summary>
        public string ModeName { get; }

        /// <summary>
        /// Токен отмены операции
        /// </summary>
        public CancellationToken CancellationToken { get; }
        
        /// <summary>
        /// Путь до сканируемого файла
        /// </summary>
        public string ScanFilePath { get; }

        /// <summary>
        /// Настройки выгрузки папки
        /// </summary>
        public ExportSettings FolderSettings { get; }

        /// <summary>
        /// Настройки проверки габарита развёртки
        /// </summary>
        public CheckFileSettings CheckSettings { get; }

        /// <summary>
        /// Кол-во выгружаемых файлов
        /// </summary>
        public int UnloadCount { get; }

        public ExportContext(Inventor.Application invApp, ObservableCollection<StructureClass> procData,
            ObservableCollection<MaskPart> maskData, string modeName, CancellationToken cancellationToken,
            string scanFilePath, ExportSettings folderSettings, CheckFileSettings checkSettings, int unloadCount)
        {
            InvApp = invApp ?? throw new ArgumentNullException(nameof(invApp));
            ProcData = procData ?? throw new ArgumentNullException(nameof(procData));
            MaskData = maskData ?? throw new ArgumentNullException(nameof(maskData));
            ModeName = modeName;
            CancellationToken = cancellationToken;
            ScanFilePath = scanFilePath;
            FolderSettings = folderSettings;
            CheckSettings = checkSettings;
            UnloadCount = unloadCount;
        }
    }

    /// <summary>
    /// Класс для выгрузки dxf
    /// </summary>
    internal class ExportProcess
    {
        private readonly MainWindow _mainWindow;
        private double _percent;

        /// <summary>
        /// Словарь для сопоставления маски и данных сканирования
        /// </summary>
        private static readonly IReadOnlyDictionary<string, Func<StructureClass, string>> _map
            = new Dictionary<string, Func<StructureClass, string>>
            {
                [HeaderConst.Обозначение] = s => s.PartNumber ?? "",
                [HeaderConst.Наименование] = s => s.Description ?? "",
                [HeaderConst.Материал] = s => s.Material ?? "",
                [HeaderConst.Толщина] = s => s.Thickness.ToString(),
                [HeaderConst.Количество] = s => s.Quantity.ToString()
            };
        

        //для ограничения отправки писем
        private static DateTime _lastErrorEmailSent = DateTime.MinValue;
        private static readonly TimeSpan _emailCooldown = TimeSpan.FromMinutes(5);

        public ExportProcess(MainWindow mainWindow)
        {
            _mainWindow = mainWindow;
        }

        /// <summary>
        /// Точка входа в логику выгрузки dxf, разделение потоков
        /// </summary>
        /// <param name="cancellationToken">Токен отмены обработки</param>
        /// <param name="procData">Список деталей для обработки</param>
        /// <param name="maskData">Маска/фильтры выгрузки</param>
        public async Task<string> StartProcessExport(CancellationToken cancellationToken, ObservableCollection<StructureClass> procData,
            ObservableCollection<MaskPart> maskData, string modeName, string scanFilePath, ExportSettings folderSettings,
            CheckFileSettings checkSettings, int unloadCount)
        {
            return await InventorHost.Instance.Value.RunAsync(async invApp =>
            {
                var context = new ExportContext(invApp, procData, maskData,
                    modeName, cancellationToken, scanFilePath, folderSettings, checkSettings, unloadCount);

                var dir = ExportProcessing(context);

                if (_mainWindow.CheckExit.NeedExit)
                {
                    _mainWindow.Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show(_mainWindow, _mainWindow.CheckExit.Message, "Выгрузка не завершена!",
                            MessageBoxButton.OK, MessageBoxImage.Stop);
                    });
                    
                    return String.Empty;
                }

                return dir;
            });

        }

        /// <summary>
        /// Метод обработка выгрузки dxf
        /// </summary>
        /// <param name="ctx">Контекстный класс</param>
        private string ExportProcessing(ExportContext ctx)
        {
            // Вызывает исключение, если приложение закрыли
            ctx.CancellationToken.ThrowIfCancellationRequested();
            string exportDir = String.Empty;
            
            try
            {
                // Проверка есть ли папка Чертежи
                exportDir = ctx.FolderSettings.UserDxfDir 
                    ?UserChoiceDir(ctx.ScanFilePath)
                    :CheckDrawDirect(ctx.ScanFilePath);

                _percent = Math.Round(1.0 / (ctx.UnloadCount), 5);
                _mainWindow.UpdateOverlay(true);
                
                //обработка файлов
                foreach (StructureClass fileStructure in ctx.ProcData)
                {
                    // Вызывает исключение, если приложение закрыли
                    ctx.CancellationToken.ThrowIfCancellationRequested();
                    
                    // Выход, если приложение сигнализирует о завершении
                    if (_mainWindow.CheckExit.NeedExit) return String.Empty;

                    _mainWindow.UpdateOverlay(CommonConstants.OverlayProcessDxf, fileStructure.DisplayName);

                    //пропускает детали, которые исключены из обработки
                    if (!fileStructure.NeedUnload)
                    {
                        _mainWindow.UpdateLog($"Пропущена деталь: {fileStructure.DisplayName}");
                        //_mainWindow.MinusProgress(_percent);
                        continue;
                    }

                    bool wasOpened = false;

                    PartDocument pDoc = GetOrOpenPartDocument(ctx.InvApp, fileStructure.Path, out wasOpened);
                    if (pDoc == null)
                        throw new Exception("Не удалось получить PartDocument.");

                    SheetMetalComponentDefinition sheetMetalCompDef = (SheetMetalComponentDefinition)pDoc.ComponentDefinition;

                    try
                    {
                        //Если деталь через состояние, то нужно переключить на состояние которое было отсканировано
                        if (fileStructure.IsModelStatePart)
                        {
                            ctx.InvApp.SilentOperation = true;
                            pDoc = NeedOpenedFile(ctx.InvApp, fileStructure.Path, out wasOpened);
                            foreach (var modelState in sheetMetalCompDef.ModelStates.Cast<ModelState>()
                                         .Where(modelState => modelState.Name == fileStructure.MemberName))
                            {
                                if (sheetMetalCompDef.ModelStates.ActiveModelState.Name == fileStructure.MemberName) break;
                                modelState.Activate();
                                break;
                            }
                        }

                        //проверка детали, есть ли развёртка
                        var createResult = CreateFlat(fileStructure, sheetMetalCompDef);
                        //если пропущена обработка переходим к следующей детали
                        if (createResult.skip)
                        {
                            continue;
                        }
                        //если создана развёртка предложить создать гравировку
                        if (createResult.create) 
                        {
                            AddGrav(ctx, pDoc, fileStructure);
                        }

                        //провести сверку материала и толщины - речь про нержу 0,7 => 0,8
                        CheckAisiThick();
                        
                        //Получение структуры папок
                        string subDir = CreateDirStructure();
                        if (!Directory.Exists(subDir)) Directory.CreateDirectory(subDir);

                        //Получение имени файла из выбранной маски выгрузки
                        string fileName = ApplyMask(ctx.MaskData, fileStructure) + ".dxf";

                        //учесть настройки выгрузки - без гравировки, с линиями гиба, все исполнения (пока не трогать)
                        //Выгрузить dxf
                        ExportDxf();
                        
                        //TODO:внедрить проверку габарита развёртки, важно! после CheckAisiThick
                        //Проверка габарита развёртки
                        //_mainWindow.UpdateLog(ctx.CheckSettings.PathTable, false);

                        _mainWindow.UpdateLog($"Выгружена: {fileName}");
                        //_mainWindow.UpdateLog($"Обработана деталь: {fileStructure.DisplayName}");
                        _mainWindow.MinusProgress(_percent);
                        _mainWindow.ChangeColor(fileStructure);
                        _mainWindow.SelectAndScrollToItem(fileStructure);

                        //внутренний метод для создания структуры папок
                        string CreateDirStructure ()
                        {
                            string dir = exportDir;

                            //Создание структуры папок в папке dxf
                            //если выгрузка в шаблоны
                            if (fileStructure.UnloadInTemplate)
                            {
                                dir = Path.Combine(dir, "Шаблоны");
                            }

                            //если нужно создать подпапку материал
                            if (ctx.FolderSettings.SubFolderMaterials)
                            {
                                dir = Path.Combine(dir, fileStructure.Material);
                            }

                            //если нужно создать подпапку толщина
                            if (ctx.FolderSettings.SubFolderThickness)
                            {
                                string thick = $"{fileStructure.Thickness} мм";
                                dir = Path.Combine(dir, thick);
                            }

                            return dir;
                        }

                        //провести сверку материала и толщины - речь про нержу 0,7 => 0,8
                        void CheckAisiThick()
                        {
                            List<string> aisi = new List<string>()
                            {
                                "aisi 321", "aisi 304", "aisi 316ti", "латунь"
                            };

                            foreach (var a in aisi.Where(a =>
                                         a.Contains(fileStructure.Material, StringComparison.CurrentCultureIgnoreCase)))
                            {
                                if (fileStructure.Thickness == 0.7)
                                    fileStructure.Thickness = 0.8;
                            }
                        }

                        //Выгрузить dxf
                        void ExportDxf()
                        {
                            // Настройки экспорта DXF
                            string acadVersion = "2000";          // или "R12", "2018" и т.д.
                            string outerLayerName = "IV_OUTER_PROFILE";
                            string interiorLayerName = "IV_INTERIOR_PROFILES";
                            string unconsumedLayerName = "IV_UNCONSUMED_SKETCHES";

                            // переменные для линий гиба
                            string bendUpLayerName = "IV_BEND";       // или "IV_Bend_Up" — проверь в своей детали
                            string bendDownLayerName = "IV_BEND_DOWN";

                            string commonLineType = "37633";         // Continuous (сплошная)
                            string dashedLineType = "37633";         // Можно поменять на штриховой, если нужно (например 37634 или другой код)

                            string commonLineWeight = "0,0500";        // 0.05 мм

                            // Цвета в формате R;G;B
                            string colorBlack = "0;0;0";
                            string colorYellow = "255;255;0";

                            // Формируем список скрытых слоёв
                            var invisibleLayersList = new List<string>
                            {
                                "IV_TANGENT",
                                "IV_ARC_CENTERS"
                            };

                            // Если НЕ нужны неиспользованные эскизы — скрываем их слой
                            if (!fileStructure.NeedGrav)
                            {
                                invisibleLayersList.Add(unconsumedLayerName);
                            }

                            // Если НЕ нужны линии гиба — скрываем их
                            if (!fileStructure.NeedBendLine)
                            {
                                invisibleLayersList.Add(bendUpLayerName);
                                invisibleLayersList.Add(bendDownLayerName);
                            }
                            string invisible = string.Join(";", invisibleLayersList);

                            // === Собираем строку параметров ===
                            string sOut = $"FLAT PATTERN DXF?AcadVersion={acadVersion}" +
                                          $"&OuterProfileLayer={outerLayerName}&OuterProfileLayerLineType={commonLineType}" +
                                          $"&OuterProfileLayerLineWeight={commonLineWeight}&OuterProfileLayerColor={colorBlack}" +

                                          $"&InteriorProfilesLayer={interiorLayerName}&InteriorProfilesLayerLineType={commonLineType}" +
                                          $"&InteriorProfilesLayerLineWeight={commonLineWeight}&InteriorProfilesLayerColor={colorBlack}" +

                                          $"&FeatureProfilesUpLayerLineType={commonLineType}&FeatureProfilesUpLayerLineWeight={commonLineWeight}" +
                                          $"&FeatureProfilesUpLayerColor={colorYellow}" +

                                          $"&FeatureProfilesDownLayerLineType={commonLineType}&FeatureProfilesDownLayerLineWeight={commonLineWeight}" +
                                          $"&FeatureProfilesDownLayerColor={colorYellow}";

                            // Только если нужны неиспользованные эскизы — добавляем их настройки
                            if (fileStructure.NeedGrav)
                            {
                                sOut += $"&UnconsumedSketchesLayer={unconsumedLayerName}" +
                                        $"&UnconsumedSketchesLayerLineType={commonLineType}" +
                                        $"&UnconsumedSketchesLayerLineWeight={commonLineWeight}" +
                                        $"&UnconsumedSketchesLayerColor={colorYellow}";
                            }

                            // Добавляем настройки линий гиба ТОЛЬКО если они нужны
                            if (fileStructure.NeedBendLine)
                            {
                                sOut +=
                                    $"&BendUpLayer={bendUpLayerName}" +
                                    $"&BendUpLayerLineType={dashedLineType}" +
                                    $"&BendUpLayerLineWeight={commonLineWeight}" +
                                    $"&BendUpLayerColor={colorYellow}" +

                                    $"&BendDownLayer={bendDownLayerName}" +
                                    $"&BendDownLayerLineType={dashedLineType}" +
                                    $"&BendDownLayerLineWeight={commonLineWeight}" +
                                    $"&BendDownLayerColor={colorYellow}";  // или другой цвет, например "0;0;255"
                            }

                            sOut += $"&InvisibleLayers={invisible}";

                            // Экспорт
                            DataIO dataIO = pDoc.ComponentDefinition.DataIO;
                            dataIO.WriteDataToFile(sOut, System.IO.Path.Combine(subDir, fileName));
                        }
                    }
                    catch (Exception ex)
                    {
                        _mainWindow.UpdateLog($"Не удалось выгрузить Dxf у детали: {fileStructure.DisplayName}");
                        _mainWindow.MinusProgress(_percent);
                        _mainWindow.ChangeColor(fileStructure,"Не удалось создать Dxf");
                        Debug.WriteLine(ex.StackTrace);
                        continue;
                    }
                    finally
                    {
                        //если сканируемый файл не совпадает с деталью выгрузки и был открыт, то закрываем его
                        if (pDoc.FullFileName != ctx.ScanFilePath && wasOpened)
                        {
                            ReleaseObject(pDoc);
                        }
                    }
                    
                }

                //Test(ctx);
                
                _mainWindow.SelectAndScrollToItem();
                _mainWindow.SetProgress(100);
                _mainWindow.UpdateLog("====================\n", false);

                WriteReport.StartWriteReport(ctx.InvApp.UserName);
            }
            catch (Exception ex)
            {
                _mainWindow.SetProgress(0);

                Debug.WriteLine(ex.StackTrace);
                _mainWindow.CheckExit.NeedExit = true;
                _mainWindow.CheckExit.Message = $"Ошибка при выгрузке Dxf";
                _mainWindow.CheckExit.SystemMessage = ex.StackTrace;
                _mainWindow.CheckExit.UserName = ctx.InvApp.UserName;

                List<string> infoList = new List<string>()
                {
                    $"\tПуть исходного файла: {ctx.ScanFilePath}",
                    $"\t{ _mainWindow.CheckExit.Message}",
                    $"\tПоследние логи:\n{_mainWindow.GetLinesAsText()}"
                };
                if (DateTime.UtcNow - _lastErrorEmailSent >= _emailCooldown)
                {
                    CommonOperations.EmailOnError(ex.Message, ex.StackTrace, ctx.InvApp.UserName, infoList);
                    _lastErrorEmailSent = DateTime.UtcNow;  // фиксируем время отправки
                }

                //throw new InvalidOperationException(ex.Message, ex);
            }
            finally
            {
                ctx.InvApp.SilentOperation = false;
            }

            return exportDir;
        }

        /// <summary>
        /// Возвращает PartDocument по полному пути: если открыт — берёт из Documents, иначе открывает.
        /// </summary>
        /// <param name="invApp">Экземпляр Inventor.Application.</param>
        /// <param name="fullPath">Полный путь к файлу.</param>
        /// <param name="wasOpened">True если документ был открыт этим методом.</param>
        /// <returns>PartDocument или null.</returns>
        private PartDocument GetOrOpenPartDocument(Inventor.Application invApp, string fullPath, out bool wasOpened)
        {
            // Изначально считаем что не открывали
            wasOpened = false;

            // 1) Пытаемся найти документ среди уже открытых по FullFileName
            foreach (var doc in from Document doc in invApp.Documents
                     where doc.DocumentType == DocumentTypeEnum.kPartDocumentObject 
                     where !string.IsNullOrWhiteSpace(doc.FullFileName) 
                     where string.Equals(doc.FullFileName, fullPath, StringComparison.OrdinalIgnoreCase) select doc)
            {
                return (PartDocument)doc;
            }

            // 2) Не нашли — открываем
            return NeedOpenedFile(invApp, fullPath, out wasOpened);
        }

        /// <summary>
        /// Функция для открытия файла
        /// </summary>
        /// <param name="invApp"></param>
        /// <param name="fullPath"></param>
        /// <param name="wasOpened"></param>
        /// <returns></returns>
        private PartDocument NeedOpenedFile(Inventor.Application invApp, string fullPath, out bool wasOpened)
        {
            PartDocument openedDoc = invApp.Documents.Open(fullPath, true) as PartDocument;
            wasOpened = openedDoc != null;

            return openedDoc;
        }
        
        /// <summary>
        /// Проверяет путь к файлу и создаёт нужную структуру папок для DXF в зависимости от наличия "_Модель" или "Модель" в пути.
        /// </summary>
        /// <param name="filePath">Полный путь к файлу</param>
        /// <returns>Путь к папке, куда следует сохранять DXF-файл</returns>
        private string CheckDrawDirect(string filePath)
        {
            string currentDir = Path.GetDirectoryName(filePath);

            // Проверяем, есть ли в пути "_Модель" или "Модель"
            //bool hasModelFolder = currentDir.Contains("\\_Модель", StringComparison.OrdinalIgnoreCase) ||
            //currentDir.Contains("\\Модель", StringComparison.OrdinalIgnoreCase);

            // Ищем САМОЕ ПЕРВОЕ (самое верхнее) вхождение модельной папки
            int indexModel = currentDir.IndexOf("\\_Модель", StringComparison.OrdinalIgnoreCase);
            if (indexModel == -1)
            {
                indexModel = currentDir.IndexOf("\\Модель", StringComparison.OrdinalIgnoreCase);
            }

            //if (hasModelFolder)
            if (indexModel >= 0)
            {
                // Идём вверх до родительской папки (та, что содержит _Модель / Модель)
                // крайне редкий случай — корень диска
                //string? parentDir = Directory.GetParent(currentDir)?.FullName ?? currentDir;
                // Нашли — берём всё до начала этой папки
                string parentDir = currentDir[..indexModel];

                // Путь к целевой папке Чертежи
                string drawingsDir = Path.Combine(parentDir, "Чертежи");
                
                // Создаём Чертежи, если нет
                if (!Directory.Exists(drawingsDir))
                {
                    Directory.CreateDirectory(drawingsDir);
                }

                // Путь к подпапке DXF
                string dxfDir = Path.Combine(drawingsDir, "DXF");
                // Создаём DXF, если нет
                if (!Directory.Exists(dxfDir))
                {
                    Directory.CreateDirectory(dxfDir);
                }

                return dxfDir;
            }
            else
            {
                // Нет ни _Модель, ни Модель → просто создаём dxf рядом с файлом
                string dxfDir = Path.Combine(currentDir, "DXF");

                if (!Directory.Exists(dxfDir))
                {
                    Directory.CreateDirectory(dxfDir);
                }

                return dxfDir;
            }

        }

        /// <summary>
        /// Выбрать папку, которую укажет пользователь.
        /// </summary>
        /// <returns>Путь к папке, куда следует сохранять DXF-файл</returns>
        private string UserChoiceDir(string filePath)
        {
            string drawingsDir = String.Empty;
            string currentDir = Path.GetDirectoryName(filePath);

            var dialog = new FolderPicker
            {
                InputPath = currentDir, //папка по-умолчанию
                Title = "Выберите папку для выгрузки DXF"    //Запрос
            };

            while (string.IsNullOrEmpty(drawingsDir)) //выполняет до тех пор, пока не будет выбрана папка
            {
                var result = dialog.ShowDialog(); //показать диалог
                if (result == true)  //обрабатывает нажатие ОК
                {
                    drawingsDir = dialog.ResultPath; //присваивает строке выделенную папку
                }
                else //в случае отмены возвращаем стандартную папку
                {
                    return CheckDrawDirect(filePath);
                }
            }

            // Путь к подпапке DXF
            string dxfDir = Path.Combine(drawingsDir, "DXF");
            // Создаём DXF, если нет
            if (!Directory.Exists(dxfDir))
            {
                Directory.CreateDirectory(dxfDir);
            }

            return dxfDir;
        }

        /// <summary>
        /// Закрываем файлы, очищаем ресурсы.
        /// </summary>
        /// <param name="doc">Файл который нужно закрыть. Передаваем динамически</param>
        private void ReleaseObject(dynamic doc)
        {
            try
            {
                doc?.Close();
                if (doc != null) Marshal.ReleaseComObject(doc);
            }
            catch (Exception e)
            {
                Debug.WriteLine(e.Message);
            }
        }

        /// <summary>
        /// Обрабатываем детали без развёртки и с пустой развёрткой
        /// </summary>
        /// <param name="fileStructure">Данные детали</param>
        /// <param name="sheetMetalCompDef">Развёртка детали</param>
        /// <returns></returns>
        private (bool create, bool skip) CreateFlat(StructureClass fileStructure, SheetMetalComponentDefinition sheetMetalCompDef)
        {
            //если у детали нет развёртки, то создать её
            if (fileStructure.NoFlat)
            {
                sheetMetalCompDef.Unfold();
                sheetMetalCompDef.FlatPattern.ExitEdit();
                fileStructure.Status = fileStructure.Status.Replace(ErrorsConst.NoFlat, "").Trim();
                return (true,false);
            }

            // если у детали пустая развёртка, то предложить пересоздать её
            if (fileStructure.NullFlat)
            {
                MessageBoxResult userResult = MessageBoxResult.None;

                _mainWindow.Dispatcher.Invoke(() =>
                {
                    userResult = MessageBox.Show(_mainWindow,
                        $"В детали:\n{fileStructure.DisplayName}\nесть развёртка, но она пустая!\nПересоздать её?", 
                        "Пустая развёртка",
                        MessageBoxButton.YesNo, MessageBoxImage.Question);
                });
                 
                if (userResult == MessageBoxResult.Yes)
                {
                    sheetMetalCompDef.FlatPattern.Delete();
                    sheetMetalCompDef.Unfold();
                    sheetMetalCompDef.FlatPattern.ExitEdit();
                    fileStructure.Status = fileStructure.Status.Replace(ErrorsConst.NullFlat, "").Trim();
                    return (true, false);
                }
                else
                {
                    _mainWindow.UpdateLog($"Пропущена деталь: {fileStructure.DisplayName}");
                    _mainWindow.MinusProgress(_percent);
                    return (false, true);
                }
            }

            return (false, false);
        }
        
        /// <summary>
        /// Метод для добавления гравировки через ilogic
        /// </summary>
        /// <param name="ctx">Класс контекст</param>
        /// <param name="pDoc">Обрабатываемый документы</param>
        /// <param name="fileStructure">Структура файла</param>
        private void AddGrav(ExportContext ctx, PartDocument pDoc, StructureClass fileStructure)
        {
            MessageBoxResult userResult = MessageBoxResult.None;

            _mainWindow.Dispatcher.Invoke(() =>
            {
                userResult = MessageBox.Show(_mainWindow,
                    $"Создать гравировку в детали:\n{fileStructure.DisplayName}",
                    "Добавить гравировку?",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);
            });

            if (userResult == MessageBoxResult.Yes)
            {
                // Получаем iLogic Automation через AddIn
                ApplicationAddIn iLogicAddIn = ctx.InvApp.ApplicationAddIns.ItemById["{3BDD8D79-2179-4B11-8A5A-257B1C0263AC}"];

                if (!iLogicAddIn.Activated)
                {
                    iLogicAddIn.Activate();
                }
                // Важно: используем dynamic, потому что точный интерфейс IiLogicAutomation
                // не всегда доступен без специальной ссылки на Autodesk.iLogic.Interfaces
                dynamic iLogicAuto = iLogicAddIn.Automation;

                // Запуск внешнего правила
                // ruleName — это имя файла без расширения .iLogicVb или просто имя правила
                iLogicAuto.RunExternalRule(pDoc, "Гравировка");
            }
        }

        /// <summary>
        /// Сопоставление маски с отсканированными данными
        /// </summary>
        /// <param name="maskParts">Маска выгрузки</param>
        /// <param name="structure">Структура файла</param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        private string ApplyMask(IReadOnlyList<MaskPart> maskParts, StructureClass structure)
        {
            if (maskParts == null) throw new ArgumentNullException(nameof(maskParts));
            if (structure == null) throw new ArgumentNullException(nameof(structure));

            var sb = new StringBuilder();

            foreach (var part in maskParts)
            {
                var text = part.Text;

                // Плейсхолдер вида "{...}"
                if (text.Length >= 3 && text[0] == '{' && text[text.Length - 1] == '}')
                {
                    var key = text.Substring(1, text.Length - 2);

                    if (_map.TryGetValue(key, out var getter))
                        sb.Append(getter(structure));
                    else
                        sb.Append(text); // если не нашли — оставляем как есть (или пусто)
                }
                else
                {
                    sb.Append(text);
                }
            }

            return sb.ToString();
        }

        private void Test(ExportContext ctx )
        {
            #region test
            //тест
            foreach (StructureClass rowClasses in ctx.ProcData)
            {
                if (!rowClasses.NeedUnload) continue;
                string text = String.Empty;
                text += $"Выгрузка\t{rowClasses.NeedUnload}\n";
                text += $"Обозначение\t{rowClasses.PartNumber}\n";
                text += $"Наименование\t{rowClasses.Description}\n";
                text += $"Путь\t{rowClasses.Path}\n";
                text += $"Материал\t{rowClasses.Material}\n";
                //text += $"Дополнительный материал\t{string.Join(";", rowClasses.AddMaterial)}\n";
                text += $"Толщина\t{rowClasses.Thickness}\n";
                text += $"Кол-во\t{rowClasses.Quantity}\n";
                text += $"Св-во выгрузки\t{rowClasses.UnloadProp}\n";
                text += $"Статус\t{rowClasses.Status}\n";
                text += $"Нужна гравировка\t{rowClasses.NeedGrav}\n";
                text += $"Нужна линия гиба\t{rowClasses.NeedBendLine}\n";
                text += $"Выгрузка в шаблон\t{rowClasses.UnloadInTemplate}\n";
                text += $"Параметрическая\t{rowClasses.IsIPart}\n";
                text += $"Через состояние\t{rowClasses.IsModelStatePart}\n";
                text += $"Выгрузить все исполнения\t{rowClasses.UnloadAllVers}\n";
                text += "---\n";

                _mainWindow.UpdateLog(text, false);
            }
            string text1 = ctx.MaskData.Aggregate("Маска:\n", (current, mask) => current + mask.Text);
            _mainWindow.UpdateLog(text1, false);

            string setText = $"\n{ctx.FolderSettings.SubFolderMaterials.ToString()}\n{ctx.FolderSettings.SubFolderThickness.ToString()}";

            _mainWindow.UpdateLog(setText, false);

            //тест
            #endregion

        }

    }
}
