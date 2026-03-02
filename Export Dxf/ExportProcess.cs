using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Printing.IndexedProperties;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Xml.Linq;
using DxfExporter.Constants;
using DxfExporter.MaskProcess;
using DxfExporter.Scanning;
using Inventor;
using netDxf;
using static System.Net.Mime.MediaTypeNames;
using static DxfExporter.MainWindow;
using IoFile = System.IO.File;
using DrawingColor = System.Drawing.Color;
using DrawingPen = System.Drawing.Pen;
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
        /// Кол-во выгружаемых файлов
        /// </summary>
        public int UnloadCount { get; }

        public ExportContext(Inventor.Application invApp, ObservableCollection<StructureClass> procData,
            ObservableCollection<MaskPart> maskData, string modeName, CancellationToken cancellationToken,
            string scanFilePath, ExportSettings folderSettings, int unloadCount)
        {
            InvApp = invApp ?? throw new ArgumentNullException(nameof(invApp));
            ProcData = procData ?? throw new ArgumentNullException(nameof(procData));
            MaskData = maskData ?? throw new ArgumentNullException(nameof(maskData));
            ModeName = modeName;
            CancellationToken = cancellationToken;
            ScanFilePath = scanFilePath;
            FolderSettings = folderSettings;
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

        // TODO: при необходимости скорректировать размер миниатюры, если изменится плотность таблицы.
        private const int FlatPreviewWidthPx = 220;
        // TODO: при необходимости скорректировать высоту миниатюры под требуемую читаемость.
        private const int FlatPreviewHeightPx = 140;

        /// <summary>
        /// Словарь для сопоставления маски и данных сканирования
        /// </summary>
        private static readonly IReadOnlyDictionary<string, Func<StructureClass, string>> _map
            = new Dictionary<string, Func<StructureClass, string>>
            {
                [HeaderConst.Обозначение] = s => s.PartNumber ?? "",
                [HeaderConst.Наименование] = s => s.Description ?? "",
                [HeaderConst.Материал] = s => s.Material ?? "",
                [HeaderConst.Толщина] = s => s.Thickness?.ToString() ?? string.Empty,
                [HeaderConst.Количество] = s => s.Quantity?.ToString() ?? string.Empty
            };
        

        //для ограничения отправки писем
        private static DateTime _lastErrorEmailSent = DateTime.MinValue;
        private static readonly TimeSpan _emailCooldown = TimeSpan.FromMinutes(5);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

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
             int unloadCount)
        {
            return await InventorHost.Instance.Value.RunAsync(async invApp =>
            {
                var context = new ExportContext(invApp, procData, maskData,
                    modeName, cancellationToken, scanFilePath, folderSettings, unloadCount);

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

                    //проверка есть ли такой путь
                    if (string.IsNullOrEmpty(fileStructure.Path) || !Path.Exists(fileStructure.Path))
                    {
                        _mainWindow.UpdateLog($"Пропущена деталь: {fileStructure.DisplayName}");
                        continue;
                    }

                    bool wasOpened = false;

                    PartDocument pDoc = CommonOperations.GetOrOpenPartDocument(ctx.InvApp, fileStructure.Path, out wasOpened);
                    if (pDoc == null)
                        throw new Exception("Не удалось получить PartDocument.");

                    SheetMetalComponentDefinition sheetMetalCompDef = (SheetMetalComponentDefinition)pDoc.ComponentDefinition;

                    try
                    {
                        //Если деталь через состояние, то нужно переключить на состояние которое было отсканировано
                        if (fileStructure.IsModelStatePart)
                        {
                            ctx.InvApp.SilentOperation = true;
                            pDoc = CommonOperations.NeedOpenedFile(ctx.InvApp, fileStructure.Path, out wasOpened);
                            sheetMetalCompDef = (SheetMetalComponentDefinition)pDoc.ComponentDefinition;

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
                        
                        //Выгрузить dxf
                        ExportDxf();

                        // Формируем миниатюру по фактически выгруженному DXF (контур/геометрия),
                        // а не по снимку 3D-вида камеры Inventor.
                        string exportedDxfPath = Path.Combine(subDir, fileName);
                        var flatPreview = BuildFlatPatternPreviewFromDxf(exportedDxfPath, fileStructure);
                        _mainWindow.Dispatcher.Invoke(() => fileStructure.FlatPatternPreview = flatPreview);
                        
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
                                string thick = $"{fileStructure.Thickness?.ToString() ?? "-"} мм";
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
                            CommonOperations.ReleaseObject(pDoc);
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
        /// Создаёт миниатюру активного вида с развёрткой и возвращает её как ImageSource.
        /// Согласно API Inventor 2023 для сохранения снимка вида используется Camera.SaveAsBitmap.
        /// </summary>
        /// <summary>
        /// Создаёт миниатюру на основе уже выгруженного DXF-файла.
        /// Это даёт вид именно развёртки (контуры DXF), а не снимок камеры модели.
        /// Линии гиба дополнительно скрываются в превью, чтобы изображение было похоже
        /// на «миниатюру файла раскроя».
        /// </summary>
        private ImageSource BuildFlatPatternPreviewFromDxf(string dxfPath, StructureClass fileStructure)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(dxfPath) || !IoFile.Exists(dxfPath))
                {
                    return null;
                }

                DxfDocument doc = DxfDocument.Load(dxfPath);
                if (doc == null)
                {
                    return null;
                }

                var segments = CollectSegmentsForPreview(doc);
                if (segments.Count == 0)
                {
                    return null;
                }

                return RenderSegmentsToBitmapSource(segments, FlatPreviewWidthPx, FlatPreviewHeightPx);
            }
            catch (Exception ex)
            {
                _mainWindow.UpdateLog($"Не удалось создать миниатюру из DXF: {fileStructure.DisplayName}. Причина: {ex.Message}");
                Debug.WriteLine(ex.StackTrace);
                return null;
            }
        }

        /// <summary>
        /// Собирает набор отрезков для отрисовки миниатюры DXF.
        /// Реализация специально сделана через reflection, чтобы работать
        /// на разных версиях netDxf, где коллекции/типы могут отличаться.
        /// </summary>
        private List<(double X1, double Y1, double X2, double Y2, bool IsAccent)> CollectSegmentsForPreview(DxfDocument doc)
        {
            var segments = new List<(double X1, double Y1, double X2, double Y2, bool IsAccent)>();
            const int arcApproxSteps = 32;

            foreach (object entity in EnumerateDxfEntities(doc))
            {
                if (entity == null)
                {
                    continue;
                }

                bool isAccent = IsAccentLayer(entity);
                string typeName = entity.GetType().Name;

                if (typeName.Equals("Line", StringComparison.OrdinalIgnoreCase))
                {
                    if (TryGetPointXY(entity, "StartPoint", out double x1, out double y1) &&
                        TryGetPointXY(entity, "EndPoint", out double x2, out double y2))
                    {
                        segments.Add((x1, y1, x2, y2, isAccent));
                    }

                    continue;
                }

                if (typeName.Contains("Polyline", StringComparison.OrdinalIgnoreCase))
                {
                    var vertices = ReadVertexPoints(entity);
                    for (int i = 0; i < vertices.Count - 1; i++)
                    {
                        segments.Add((vertices[i].X, vertices[i].Y, vertices[i + 1].X, vertices[i + 1].Y, isAccent));
                    }

                    bool isClosed = TryGetBool(entity, "IsClosed");
                    if (isClosed && vertices.Count > 1)
                    {
                        segments.Add((vertices[^1].X, vertices[^1].Y, vertices[0].X, vertices[0].Y, isAccent));
                    }

                    continue;
                }

                if (typeName.Equals("Arc", StringComparison.OrdinalIgnoreCase))
                {
                    if (TryGetPointXY(entity, "Center", out double cx, out double cy) &&
                        TryGetDouble(entity, "Radius", out double r) &&
                        TryGetDouble(entity, "StartAngle", out double startDeg) &&
                        TryGetDouble(entity, "EndAngle", out double endDeg))
                    {
                        double start = DegreesToRadians(startDeg);
                        double end = DegreesToRadians(endDeg);
                        if (end < start) end += Math.PI * 2.0;

                        double prevX = cx + r * Math.Cos(start);
                        double prevY = cy + r * Math.Sin(start);

                        for (int i = 1; i <= arcApproxSteps; i++)
                        {
                            double t = start + (end - start) * i / arcApproxSteps;
                            double nextX = cx + r * Math.Cos(t);
                            double nextY = cy + r * Math.Sin(t);
                            segments.Add((prevX, prevY, nextX, nextY, isAccent));
                            prevX = nextX;
                            prevY = nextY;
                        }
                    }

                    continue;
                }

                if (typeName.Equals("Circle", StringComparison.OrdinalIgnoreCase))
                {
                    if (TryGetPointXY(entity, "Center", out double cx, out double cy) &&
                        TryGetDouble(entity, "Radius", out double r))
                    {
                        double prevX = cx + r;
                        double prevY = cy;

                        for (int i = 1; i <= arcApproxSteps; i++)
                        {
                            double t = Math.PI * 2.0 * i / arcApproxSteps;
                            double x = cx + r * Math.Cos(t);
                            double y = cy + r * Math.Sin(t);
                            segments.Add((prevX, prevY, x, y, isAccent));
                            prevX = x;
                            prevY = y;
                        }
                    }
                }
            }

            return segments;
        }

        private IEnumerable<object> EnumerateDxfEntities(DxfDocument doc)
        {
            if (doc?.Entities == null)
            {
                yield break;
            }

            // В одних версиях netDxf Entities может быть перечислимым напрямую,
            // в других — доступ к полному списку идёт через свойство All.
            object entitiesObject = doc.Entities;
            var directEnumerable = entitiesObject as IEnumerable;
            if (directEnumerable != null)
            {
                foreach (var item in directEnumerable)
                {
                    if (item != null) yield return item;
                }

                yield break;
            }

            var allProp = doc.Entities.GetType().GetProperty("All");
            if (allProp?.GetValue(doc.Entities) is IEnumerable allEnumerable)
            {
                foreach (var item in allEnumerable)
                {
                    if (item != null) yield return item;
                }
            }
        }

        private bool IsAccentLayer(object entity)
        {
            var layerProp = entity.GetType().GetProperty("Layer");
            object layerObj = layerProp?.GetValue(entity);
            if (layerObj == null)
            {
                return false;
            }

            var nameProp = layerObj.GetType().GetProperty("Name");
            string layerName = nameProp?.GetValue(layerObj)?.ToString() ?? string.Empty;

            return layerName.Contains("BEND", StringComparison.OrdinalIgnoreCase) ||
                   layerName.Contains("UNCONSUMED", StringComparison.OrdinalIgnoreCase) ||
                   layerName.Contains("SKETCH", StringComparison.OrdinalIgnoreCase) ||
                   layerName.Contains("ENGRAV", StringComparison.OrdinalIgnoreCase) ||
                   layerName.Contains("ГРАВ", StringComparison.OrdinalIgnoreCase);
        }

        private List<(double X, double Y)> ReadVertexPoints(object polylineEntity)
        {
            var result = new List<(double X, double Y)>();
            var vertexesProp = polylineEntity.GetType().GetProperty("Vertexes");
            if (!(vertexesProp?.GetValue(polylineEntity) is IEnumerable vertices))
            {
                return result;
            }

            foreach (var vertex in vertices)
            {
                if (TryGetPointXY(vertex, "Position", out double x, out double y))
                {
                    result.Add((x, y));
                }
            }

            return result;
        }

        private bool TryGetPointXY(object owner, string propertyName, out double x, out double y)
        {
            x = 0;
            y = 0;

            var prop = owner.GetType().GetProperty(propertyName);
            object pointObj = prop?.GetValue(owner);
            if (pointObj == null)
            {
                return false;
            }

            var type = pointObj.GetType();
            var xProp = type.GetProperty("X");
            var yProp = type.GetProperty("Y");
            if (xProp == null || yProp == null)
            {
                return false;
            }

            x = Convert.ToDouble(xProp.GetValue(pointObj));
            y = Convert.ToDouble(yProp.GetValue(pointObj));
            return true;
        }

        private bool TryGetDouble(object owner, string propertyName, out double value)
        {
            value = 0;
            var prop = owner.GetType().GetProperty(propertyName);
            object raw = prop?.GetValue(owner);
            if (raw == null)
            {
                return false;
            }

            value = Convert.ToDouble(raw);
            return true;
        }

        private bool TryGetBool(object owner, string propertyName)
        {
            var prop = owner.GetType().GetProperty(propertyName);
            object raw = prop?.GetValue(owner);
            return raw != null && Convert.ToBoolean(raw);
        }

        /// <summary>
        /// Рендерит набор DXF-сегментов в миниатюру WPF ImageSource.
        /// </summary>
        private ImageSource RenderSegmentsToBitmapSource(List<(double X1, double Y1, double X2, double Y2, bool IsAccent)> segments, int width, int height)
        {
            using var bmp = new Bitmap(width, height);
            using var g = Graphics.FromImage(bmp);

            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
            g.Clear(DrawingColor.White);

            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;

            foreach (var s in segments)
            {
                minX = Math.Min(minX, Math.Min(s.X1, s.X2));
                minY = Math.Min(minY, Math.Min(s.Y1, s.Y2));
                maxX = Math.Max(maxX, Math.Max(s.X1, s.X2));
                maxY = Math.Max(maxY, Math.Max(s.Y1, s.Y2));
            }

            double srcW = Math.Max(1e-6, maxX - minX);
            double srcH = Math.Max(1e-6, maxY - minY);
            double padding = 6.0; // TODO: при необходимости скорректировать поля миниатюры.
            double sx = (width - 2 * padding) / srcW;
            double sy = (height - 2 * padding) / srcH;
            double scale = Math.Min(sx, sy);

            double dx = (width - srcW * scale) / 2.0;
            double dy = (height - srcH * scale) / 2.0;

            using var mainPen = new DrawingPen(DrawingColor.Black, 1.0f);
            using var accentPen = new DrawingPen(DrawingColor.FromArgb(255, 219, 0), 1.0f);

            foreach (var s in segments)
            {
                float x1 = (float)(dx + (s.X1 - minX) * scale);
                float y1 = (float)(height - (dy + (s.Y1 - minY) * scale));
                float x2 = (float)(dx + (s.X2 - minX) * scale);
                float y2 = (float)(height - (dy + (s.Y2 - minY) * scale));
                g.DrawLine(s.IsAccent ? accentPen : mainPen, x1, y1, x2, y2);
            }

            IntPtr hBitmap = bmp.GetHbitmap();
            try
            {
                BitmapSource bitmapSource = Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap,
                    IntPtr.Zero,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());

                bitmapSource.Freeze();
                return bitmapSource;
            }
            finally
            {
                DeleteObject(hBitmap);
            }
        }

        private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;



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
            //для пользовательского выбора не создаём подпапку DXF
            //string dxfDir = Path.Combine(drawingsDir, "DXF");
            // Создаём DXF, если нет
            if (!Directory.Exists(drawingsDir))
            {
                Directory.CreateDirectory(drawingsDir);
            }

            return drawingsDir;
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
