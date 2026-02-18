using Inventor;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using DxfExporter.Constants;
using System.Windows.Controls;
using DxfExporter.Export_Dxf;
using static DxfExporter.MainWindow;
using System.Windows.Media.Animation;
using System.Xml.Linq;
using Path = System.IO.Path;

namespace DxfExporter.Scanning
{
    internal class ScanningProcess
    {
        private readonly MainWindow _mainWindow;
        private Inventor.Application _invApp = null;
        private Document _scanDoc = null;
        private double _percent;

        //для ограничения отправки писем
        private static DateTime _lastErrorEmailSent = DateTime.MinValue;
        private static readonly TimeSpan _emailCooldown = TimeSpan.FromMinutes(5);
        
        /// <summary>
        /// Настройки проверки габарита развёртки
        /// </summary>
        public CheckFileSettings CheckSettings { get; }

        //public StateWithMessage CheckExit = new StateWithMessage { };

        /// <summary>
        /// Для обработки потока
        /// </summary>
        //private ManualResetEvent _completionEvent = new ManualResetEvent(false);

        public ScanningProcess(MainWindow mainWindow, CheckFileSettings checkSettings)
        {
            _mainWindow = mainWindow;
            CheckSettings = checkSettings;
        }
        
        /// <summary>
        /// Точка входа в логику обработки, разделение потоков
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<ScanResult> StartProcessingAsync(CancellationToken cancellationToken, 
            CallSource callSource, List<string> choiceFiles = null)
        {
            // Выполняем обработку в STA-потоке Inventor через InventorHost
            return await InventorHost.Instance.Value.RunAsync(async invApp =>
            {
                _invApp = invApp;
                ScanResult result = null;
                string msgText = String.Empty;

                // логика работы с Inventor
                switch (callSource)
                {
                    case CallSource.Scan:
                        result = DataProcessing(cancellationToken);
                        msgText = "Сканирование не завершено!";
                        break;
                    case CallSource.AddOpen:
                        result = AddProcessing(cancellationToken);
                        msgText = "Не удалось добавить открытые файлы!";
                        break;
                    case CallSource.ChoiceInFolder:
                        result = AddProcessing(cancellationToken, choiceFiles);
                        msgText = "Не удалось добавить файлы из папки!";
                        break;
                }

                //MessageBox.Show("Test");

                if (_mainWindow.CheckExit.NeedExit)
                {
                    _mainWindow.Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show(_mainWindow.CheckExit.Message, msgText,
                            MessageBoxButton.OK, MessageBoxImage.Stop);
                    });
                    return null;
                }

                return result;
            });
        }

        /// <summary>
        /// Метод обработки файлов
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        private ScanResult DataProcessing(CancellationToken cancellationToken)
        {
            // Вызывает исключение, если приложение закрыли
            cancellationToken.ThrowIfCancellationRequested();

            ScanResult result = new ScanResult();

            Document doc = _invApp.ActiveDocument;
            _scanDoc = doc;

            try
            {
                // Проверка активного документа в Inventor
                CheckActiveDocument(doc);

                // Если нужно выйти
                if (_mainWindow.CheckExit.NeedExit)
                {
                    _mainWindow.UpdateLog(_mainWindow.CheckExit.Message, false);
                    return null;
                }

                _mainWindow._scanFilePath = doc.FullFileName;
                string modName = string.Empty;

                switch (doc.DocumentType)
                {
                    //обработка детали
                    case DocumentTypeEnum.kPartDocumentObject:
                        _mainWindow.UpdateOverlay(true);
                        _mainWindow.UpdateOverlay(CommonConstants.OverlayProcessScan,doc.DisplayName);
                        
                        modName = doc.ModelStateName;
                        result.AddNode(ProcessPart((PartDocument)doc, modName));
                        break;
                    //обработка сборки
                    case DocumentTypeEnum.kAssemblyDocumentObject:
                        //проверка потерянных ссылок
                        //if (HasMissingReferences((AssemblyDocument)doc))
                        //{
                        //    _mainWindow.CheckExit.NeedExit = true;
                        //    _mainWindow.CheckExit.Message = "В сборке есть потерянные ссылки. Необходимо скорректировать сборку.";
                        //    return null;
                        //}

                        _mainWindow.UpdateOverlay(CommonConstants.OverlayScan);

                        //подсчёт кол-ва всех элементов
                        int totalCount = GetCount((AssemblyDocument)doc);
                        _percent = Math.Round(1.0 / (totalCount +1 ), 5);
                        _mainWindow.UpdateOverlay(true);

                        //сканирование сборки, сбор листовых деталей
                        OccScanResult occResult = new OccScanResult();
                        ScanOccurrences(cancellationToken, (AssemblyDocument)doc, occResult);

                        //запись в таблицу найденных деталей
                        _mainWindow.UpdateOverlay(CommonConstants.OverlayProcessData);
                        _mainWindow.UpdateOverlay(false);
                        _mainWindow.UpdateLog("====================\n", false);

                        foreach (OccStructure occStructure in occResult.ScannedOcc)
                        {
                            // Вызывает исключение, если приложение закрыли
                            cancellationToken.ThrowIfCancellationRequested();

                            PartDocument pDoc = occStructure.Occurrence.Definition.Document;
                            modName = occStructure.MemberName;
                            result.AddNode(ProcessPart(pDoc, modName, occStructure.Quantity));
                        }

                        break;
                }

                _mainWindow.UpdateLog("====================\n", false);
                _mainWindow.SetProgress(100);
            }
            catch (Exception ex)
            {
                _mainWindow.SetProgress(0);

                Debug.WriteLine(ex.StackTrace);
                _mainWindow.CheckExit.NeedExit = true;
                _mainWindow.CheckExit.Message = $"Ошибка при сканировании сборки: {doc.DisplayName}";
                _mainWindow.CheckExit.SystemMessage = ex.StackTrace;
                _mainWindow.CheckExit.UserName = _invApp.UserName;

                List<string> infoList = new List<string>()
                {
                    $"\tПуть до файла: {_invApp.ActiveDocument.FullFileName}",
                    $"\t{ _mainWindow.CheckExit.Message}",
                    $"\tПоследние логи:\n{_mainWindow.GetLinesAsText()}"
                };
                if (DateTime.UtcNow - _lastErrorEmailSent >= _emailCooldown)
                {
                    CommonOperations.EmailOnError(ex.Message, ex.StackTrace, _invApp.UserName, infoList);
                    _lastErrorEmailSent = DateTime.UtcNow;  // фиксируем время отправки
                }

                //throw new InvalidOperationException(ex.Message,ex);
            }
            finally
            {

            }

            return result;
        }
        
        /// <summary>
        /// Проверка файла Inventor
        /// </summary>
        private void CheckActiveDocument(Document doc)
        {
            _mainWindow.UpdateOverlay(CommonConstants.OverlayCheckFile);

            if (doc == null)
            {
                _mainWindow.CheckExit.NeedExit = true;
                _mainWindow.CheckExit.Message = "В Inventor нет активного файла!";
                return;
            }

            if (string.IsNullOrEmpty(doc.FullFileName))
            {
                _mainWindow.CheckExit.NeedExit = true;
                _mainWindow.CheckExit.Message = "Файл не сохранён!";
                return;
            }
            
            if (doc.DocumentType != DocumentTypeEnum.kAssemblyDocumentObject && doc.DocumentType != DocumentTypeEnum.kPartDocumentObject)
            {
                _mainWindow.CheckExit.NeedExit = true;
                _mainWindow.CheckExit.Message = "Активный документ в Inventor не является сборкой или деталью! Выберите другой документ.";
                return;
            }

            if (doc.DocumentType is DocumentTypeEnum.kAssemblyDocumentObject)
            {
                if (((AssemblyDocument)doc).ComponentDefinition.Occurrences.Count == 0)
                {
                    _mainWindow.CheckExit.NeedExit = true;
                    _mainWindow.CheckExit.Message = "В сборке нет деталей!";
                    return;
                }
            }
            else if (doc.DocumentType is DocumentTypeEnum.kPartDocumentObject)
            {
                PartDocument pDoc = (PartDocument)doc;
                if (pDoc.ComponentDefinition.SurfaceBodies.Count > 1)
                {
                    _mainWindow.CheckExit.NeedExit = true;
                    _mainWindow.CheckExit.Message = "Многотельная деталь! Выберите обычную деталь.";
                    return;
                }

                if (pDoc.SubType != SubType.ЛистоваяДеталь)
                {
                    _mainWindow.CheckExit.NeedExit = true;
                    _mainWindow.CheckExit.Message = "Выбрана не листовая деталь!";
                    return;
                }
            }

            

        }

        /// <summary>
        /// Проверяет сборку и её подсборки на наличие отсутствующих ссылок.
        /// </summary>
        /// <param name="assemDoc">Объект сборки (AssemblyDocument).</param>
        /// <returns>True, если есть отсутствующие ссылки, иначе False.</returns>
        private bool HasMissingReferences(AssemblyDocument assemDoc)
        {
            try
            {
                foreach (var compOcc in assemDoc.ComponentDefinition.Occurrences.Cast<ComponentOccurrence>()
                             .Where(compOcc => !compOcc.Suppressed && !compOcc.Excluded))
                {
                    // Проверка, отсутствует ли ссылка на компонент
                    if (compOcc.ReferencedDocumentDescriptor.ReferenceMissing)
                        return true;

                    // Если компонент является сборкой, рекурсивно проверяем её
                    if (compOcc.Definition.Document.DocumentType == (int)DocumentTypeEnum.kAssemblyDocumentObject)
                    {
                        //Пропускать сварные сборки
                        if (compOcc.Definition.Type == ObjectTypeEnum.kWeldsComponentDefinitionObject) continue;

                        if (HasMissingReferences(compOcc.Definition.Document as AssemblyDocument))
                            return true;
                    }
                }

                return false;
            }
            catch
            {
                return false; // В случае ошибки возвращаем false
            }
        }

        /// <summary>
        /// Подсчитываем кол-во всех элементов в сборке
        /// </summary>
        /// <param name="assemDoc"></param>
        /// <returns></returns>
        private int GetCount(AssemblyDocument assemDoc)
        {
            int count = 0;

            void CountRecursive(AssemblyDocument asm)
            {
                count += asm.ComponentDefinition.Occurrences.Count;

                foreach (var occ in asm.ComponentDefinition.Occurrences.Cast<ComponentOccurrence>()
                             .Where(occ => occ.DefinitionDocumentType == DocumentTypeEnum.kAssemblyDocumentObject))
                {
                    if (occ.Excluded || occ.Suppressed) continue;
                    if (occ.ReferencedDocumentDescriptor is { ReferenceMissing: true }) continue;

                    //Пропускать сварные сборки
                    if (occ.Definition.Type == ObjectTypeEnum.kWeldsComponentDefinitionObject) continue;
                    
                    CountRecursive((AssemblyDocument)occ.Definition.Document);
                }
            }

            CountRecursive(assemDoc);
            return count;
        }

        /// <summary>
        /// Рекурсивный обход всех компонентов сборки для анализа структуры и свойств.
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <param name="asmDocument">Документ сборки Inventor</param>
        /// <param name="occResult">Результат сканирования для накопления данных</param>
        private void ScanOccurrences(CancellationToken cancellationToken, AssemblyDocument asmDocument,
            OccScanResult occResult)
        {
            foreach (ComponentOccurrence componentOccurrence in asmDocument.ComponentDefinition.Occurrences)
            {
                // Вызывает исключение, если приложение закрыли
                cancellationToken.ThrowIfCancellationRequested();

                // Выход, если приложение сигнализирует о завершении
                if (_mainWindow.CheckExit.NeedExit) return;

                string nameOcc = componentOccurrence.Name;
                _mainWindow.UpdateOverlay(CommonConstants.OverlayProcessScan, nameOcc);

                try
                { 
                    // пропускает исключенные или подавленные элементы
                    if (componentOccurrence.Suppressed || componentOccurrence.Excluded)
                    {
                        
                        _mainWindow.UpdateLog($"Пропущена деталь: {nameOcc}");
                        _mainWindow.MinusProgress(_percent);
                        continue; 
                    }

                    // пропустить стандартные изделия
                    if (componentOccurrence.BOMStructure is BOMStructureEnum.kPurchasedBOMStructure)
                    {
                        _mainWindow.UpdateLog($"Пропущена деталь: {nameOcc}");
                        _mainWindow.MinusProgress(_percent);
                        continue;
                    }

                    // пропустить детали с потерянными ссылками
                    if (componentOccurrence.ReferencedDocumentDescriptor is { ReferenceMissing: true })
                    {
                        _mainWindow.UpdateLog($"Пропущена деталь: {nameOcc}");
                        _mainWindow.MinusProgress(_percent);
                        continue;
                    }

                    // пропустить детали из Content center
                    if (componentOccurrence.Definition is PartComponentDefinition def && def.IsContentMember)
                    {
                        _mainWindow.UpdateLog($"Пропущена деталь: {nameOcc}");
                        _mainWindow.MinusProgress(_percent);
                        continue;
                    }

                    // проверяем сборка или деталь
                    if (componentOccurrence.DefinitionDocumentType == DocumentTypeEnum.kAssemblyDocumentObject)
                    {
                        //Пропускать сварные сборки
                        if (componentOccurrence.Definition.Type == ObjectTypeEnum.kWeldsComponentDefinitionObject) continue;

                        AssemblyDocument asmDoc = componentOccurrence.Definition.Document;
                        // пропускаем сборку если в ней нет деталей
                        if (asmDoc.ComponentDefinition.Occurrences.Count == 0)
                        {
                            _mainWindow.UpdateLog($"Пропущена сборка: {nameOcc}");
                            _mainWindow.MinusProgress(_percent);
                            continue;
                        }

                        ScanOccurrences(cancellationToken, asmDoc, occResult);

                        _mainWindow.UpdateLog($"Обработана сборка: {nameOcc}");
                        _mainWindow.MinusProgress(_percent);
                    }
                    else if (componentOccurrence.DefinitionDocumentType == DocumentTypeEnum.kPartDocumentObject)
                    {
                        PartDocument pDoc = (PartDocument)componentOccurrence.Definition.Document;
                        // пропускаем многотельную деталь
                        if (pDoc.ComponentDefinition.SurfaceBodies.Count > 1)
                        {
                            _mainWindow.UpdateLog($"Пропущена деталь: {nameOcc}");
                            _mainWindow.MinusProgress(_percent);
                            continue;
                        }

                        // пропускаем не листовую деталь
                        if (pDoc.SubType != SubType.ЛистоваяДеталь)
                        {
                            _mainWindow.UpdateLog($"Пропущена деталь: {nameOcc}");
                            _mainWindow.MinusProgress(_percent);
                            continue;
                        }
                        
                        //Создание структуры
                        OccStructure structure = new OccStructure()
                        {
                            Path = componentOccurrence.ReferencedDocumentDescriptor.FullDocumentName,
                            Quantity = 1,
                            Occurrence = componentOccurrence,
                            MemberName = componentOccurrence.ActiveModelState ?? string.Empty
                        };

                        occResult.AddNode(structure);

                        _mainWindow.UpdateLog($"Обработана деталь: {nameOcc}");
                        _mainWindow.MinusProgress(_percent);
                    }

                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex.StackTrace);
                    _mainWindow.UpdateLog($"Ошибка при сканировании: {nameOcc}");
                    continue;
                }
                finally
                {

                }

            }

        }


        /// <summary>
        /// Рекурсивный обход всех компонентов сборки для анализа структуры и свойств.
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <param name="asmDocument">Документ сборки Inventor</param>
        /// <param name="result">Результат сканирования для накопления данных</param>
        private void TraverseOccurrences(CancellationToken cancellationToken, AssemblyDocument asmDocument,
            ScanResult result, double quantity = 1)
        {
            HashSet<string> hashOcc = new HashSet<string>();

            try
            {
                foreach (ComponentOccurrence componentOccurrence in asmDocument.ComponentDefinition.Occurrences)
                {
                    // Вызывает исключение, если приложение закрыли
                    cancellationToken.ThrowIfCancellationRequested();
                    // Выход, если приложение сигнализирует о завершении
                    if (_mainWindow.CheckExit.NeedExit) return;
                    
                    // пропускает исключенные или подавленные элементы
                    if (componentOccurrence.Suppressed || componentOccurrence.Excluded) { continue; }

                    // пропустить стандартные изделия
                    if (componentOccurrence.BOMStructure is BOMStructureEnum.kPurchasedBOMStructure) { continue; }

                    // пропустить детали из Content center
                    if (componentOccurrence.Definition is PartComponentDefinition def && def.IsContentMember) { continue; }
                    
                    // для обработки только 1 раз
                    string fullDocName = componentOccurrence.ReferencedDocumentDescriptor.FullDocumentName;
                    if (!hashOcc.Add(fullDocName)) { continue; }

                    // проверяем сборка или деталь
                    if (componentOccurrence.DefinitionDocumentType == DocumentTypeEnum.kAssemblyDocumentObject)
                    {
                        AssemblyDocument asmDoc = componentOccurrence.Definition.Document;
                        // пропускаем сборку если в ней нет деталей
                        if (asmDoc.ComponentDefinition.Occurrences.Count == 0) { continue; }

                        _mainWindow.UpdateOverlay(CommonConstants.OverlayProcessScanAsm, asmDoc.DisplayName);

                        TraverseOccurrences(cancellationToken, asmDoc, result);

                        //_mainWindow.MinusProgress(1 / _allCount);
                    }
                    else if (componentOccurrence.DefinitionDocumentType == DocumentTypeEnum.kPartDocumentObject)
                    {
                        PartDocument pDoc = (PartDocument)componentOccurrence.Definition.Document;
                        // пропускаем многотельную деталь
                        if (pDoc.ComponentDefinition.SurfaceBodies.Count > 1) { continue; }

                        // пропускаем не листовую деталь
                        if (pDoc.SubType != SubType.ЛистоваяДеталь) { continue; }

                        _mainWindow.UpdateOverlay(CommonConstants.OverlayProcessScan, pDoc.DisplayName);
                    }


                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.StackTrace);
            }
            finally
            {

            }


        }


        /// <summary>
        /// Заполнение структуры детали
        /// </summary>
        /// <param name="partDoc">Деталь</param>
        private StructureClass ProcessPart(PartDocument partDoc,string modName,double quantity = 1, bool includeChildScan = true)
        {
            SheetMetalComponentDefinition sheetMetalCompDef = (SheetMetalComponentDefinition)partDoc.ComponentDefinition;

            string partNumber = (string)GetPropertyValue(partDoc.PropertySets, "Design Tracking Properties",
                "Part Number", "N/A");
            string description = (string)GetPropertyValue(partDoc.PropertySets, "Design Tracking Properties",
                "Description", "N/A");
            string material = sheetMetalCompDef.Material.Name;
            double thick = Math.Round((double)sheetMetalCompDef.Thickness.Value * 10, 2);
            double realThick = Math.Round(GetThickFromPart(sheetMetalCompDef) * 10, 2);

            bool isBigFlat = false;
            bool errorMatThick = false;
            if (CheckSettings.CheckGab)
            {
                var check = CheckFlat(material, thick, sheetMetalCompDef);
                isBigFlat = check.BigFlat;
                errorMatThick = check.errMatThick;
            }

            //Создание структуры
            StructureClass structure = new StructureClass
            {
                //NeedUnload = true, - по умолчанию
                PartNumber = partNumber,
                Description = description,
                Path = partDoc.FullFileName,
                Material = material,
                Thickness = thick,
                Quantity = quantity,
                DisplayName = partDoc.DisplayName,
                //UnloadProp = String.Empty, - по умолчанию
                //Status = String.Empty, - по умолчанию
                //NeedGrav = true, - по умолчанию
                //NeedBendLine = false, - по умолчанию
                //UnloadInTemplate = false, - по умолчанию
                IsIPart = sheetMetalCompDef.IsiPartFactory || sheetMetalCompDef.IsiPartMember,
                IsModelStatePart = sheetMetalCompDef.IsModelStateFactory || sheetMetalCompDef.IsModelStateMember,
                MemberName = modName,
                //UnloadAllVers = false - по умолчанию
                NoFlat = !sheetMetalCompDef.HasFlatPattern,
                NullFlat = sheetMetalCompDef.FlatPattern?.MassProperties.Mass == 0,
                BigFlat = isBigFlat,
                ErrorMatThick = errorMatThick,
                FakeThickness = realThick == 0 ? false : realThick != thick
            };
            
            if (includeChildScan && (structure.IsIPart || structure.IsModelStatePart))
            {
                _mainWindow.UpdateOverlay(true);
                _mainWindow.UpdateOverlay(CommonConstants.OverlayProcessDataIpart, partDoc.DisplayName);

                // TODO: Проверить корректность обхода и переключения всех исполнений/состояний детали.
                FillChildMembersForVersionPart(partDoc, structure, quantity);

                _mainWindow.UpdateOverlay(false);
                _mainWindow.UpdateOverlay(CommonConstants.OverlayProcessData);
            }

            _mainWindow.UpdateLog($"Записана в таблицу деталь: {partDoc.DisplayName}");

            return structure;
        }

        /// <summary>
        /// Заполнение структуры не существующей детали (для исполнений)
        /// </summary>
        /// <param name="memberPath">Путь до исполнения</param>
        private StructureClass ProcessPart(string memberPath)
        {
            string name = Path.GetFileNameWithoutExtension(memberPath);
            //Создание структуры
            StructureClass structure = new StructureClass
            {
                //NeedUnload = true, - по умолчанию
                PartNumber = name,
                Description = string.Empty,
                Path = string.Empty,
                Material = string.Empty,
                Thickness = null,
                Quantity = null,
                DisplayName = string.Empty,
                //UnloadProp = String.Empty, - по умолчанию
                //Status = String.Empty, - по умолчанию
                //NeedGrav = true, - по умолчанию
                //NeedBendLine = false, - по умолчанию
                //UnloadInTemplate = false, - по умолчанию
                IsIPart = true,
                IsModelStatePart = false,
                MemberName = string.Empty,
                //UnloadAllVers = false - по умолчанию
                NoFlat = true,
                NullFlat = true,
                BigFlat = false,
                ErrorMatThick = false,
                FakeThickness = false,
                Status = "Данное исполнение не выгружено!"
            };

            return structure;
        }


        /// <summary>
        /// Собирает дочерние исполнения/состояния для параметрических и model-state деталей.
        /// </summary>
        private void FillChildMembersForVersionPart(PartDocument partDoc, StructureClass parentStructure, double quantity)
        {
            bool wasOpened = false;
            PartDocument pDoc = null;
            _invApp.SilentOperation = true;

            try
            {
                if (parentStructure.IsModelStatePart)
                {
                    pDoc = CommonOperations.NeedOpenedFile(_invApp, partDoc.FullFileName, out wasOpened);

                    foreach (ModelState modelState in (pDoc.ComponentDefinition as SheetMetalComponentDefinition).ModelStates)
                    {
                        modelState.Activate();
                        var child = ProcessPart(pDoc, modelState.Name, quantity, includeChildScan: false);
                        parentStructure.ChildMembers.Add(child);
                    }
                }
                else if (parentStructure.IsIPart)
                {
                    List<string> memberList = GetMember(partDoc);
                    if (memberList.Count == 0) return;

                    foreach (string memberPath in memberList)
                    {
                        if (!Path.Exists(memberPath))
                        {
                            var nullChild = ProcessPart(memberPath);
                            parentStructure.ChildMembers.Add(nullChild);
                            continue;
                        }

                        var subDoc = CommonOperations.NeedOpenedFile(_invApp, memberPath, out wasOpened);

                        var child = ProcessPart(subDoc, subDoc.ModelStateName, quantity, includeChildScan: false);
                        parentStructure.ChildMembers.Add(child);

                        //если сканируемый файл не совпадает с деталью выгрузки и был открыт, то закрываем его
                        if (subDoc != null && subDoc?.FullFileName != _scanDoc.FullFileName && wasOpened)
                        {
                            CommonOperations.ReleaseObject(subDoc);
                            wasOpened = false;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
                throw;
            }
            finally
            {
                //если сканируемый файл не совпадает с деталью выгрузки и был открыт, то закрываем его
                if (pDoc != null && pDoc?.FullFileName != _scanDoc.FullFileName && wasOpened)
                {
                    CommonOperations.ReleaseObject(pDoc);
                }
                _invApp.SilentOperation = false;
            }
        }
        
        /// <summary>
        /// Получает список путей до исполнений параметрической детали. 
        /// </summary>
        /// <param name="partDoc">Параметрическая деталь.</param>
        /// <returns>Список путей до исполнений.</returns>
        private List<string> GetMember(PartDocument partDoc)
        {
            List<string> membersPath = new List<string>();
            PartDocument? factoryDoc = null;

            SheetMetalComponentDefinition compDef = partDoc.ComponentDefinition as SheetMetalComponentDefinition;
            if (compDef.IsiPartMember)
            {
                factoryDoc = compDef.iPartMember.ReferencedDocumentDescriptor.ReferencedDocument as PartDocument;
            }

            if (compDef.IsiPartFactory)
            {
                factoryDoc = partDoc;
            }

            string dirPath = factoryDoc.ComponentDefinition.iPartFactory.MemberCacheDir;

            foreach (iPartTableCell o in factoryDoc.ComponentDefinition.iPartFactory.FileNameColumn)
            {
                //замена запрещёных символов через словарь
                string memberName = CommonOperations.ReplaceDictionary
                    .Aggregate(o.Value, (current, kvp) => current.Replace(kvp.Key, kvp.Value));

                membersPath.Add(Path.Combine(dirPath,memberName)+".ipt");
            }

            return membersPath;
        }

        /// <summary>
        /// Получаем свойства.
        /// </summary>
        /// <param name="propSets">Набор свойств.</param>
        /// <param name="setName">Имя набора.</param>
        /// <param name="propName">Имя свойства.</param>
        /// <param name="defaultValue">Стандартное значение.</param>
        private object GetPropertyValue(PropertySets propSets, string setName, string propName, object defaultValue = null)
        {
            try
            {
                Property prop = propSets[setName][propName];
                return prop?.Value ?? defaultValue;
            }
            catch
            {
                return defaultValue;
            }
        }


        /// <summary>
        /// Метод добавления файлов
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        private ScanResult AddProcessing(CancellationToken cancellationToken)
        {
            // Вызывает исключение, если приложение закрыли
            cancellationToken.ThrowIfCancellationRequested();

            ScanResult result = new ScanResult();
            string docFilePath = string.Empty;

            try
            {
                //проходим по всем открытым видимым документам
                foreach (var pDoc in 
                         from _Document documentsVisibleDocument in _invApp.Documents.VisibleDocuments 
                         where documentsVisibleDocument.DocumentType == DocumentTypeEnum.kPartDocumentObject 
                         select (PartDocument)documentsVisibleDocument)
                {
                    // Вызывает исключение, если приложение закрыли
                    cancellationToken.ThrowIfCancellationRequested();

                    // Если нужно выйти
                    if (_mainWindow.CheckExit.NeedExit)
                    {
                        _mainWindow.UpdateLog(_mainWindow.CheckExit.Message, false);
                        return null;
                    }

                    // пропускаем многотельную деталь
                    if (pDoc.ComponentDefinition.SurfaceBodies.Count > 1)
                    {
                        _mainWindow.UpdateLog($"Пропущена деталь: {pDoc.DisplayName}");
                        continue;
                    }

                    // пропускаем не листовую деталь
                    if (pDoc.SubType != SubType.ЛистоваяДеталь)
                    {
                        _mainWindow.UpdateLog($"Пропущена деталь: {pDoc.DisplayName}");
                        continue;
                    }

                    docFilePath = pDoc.FullFileName;
                    if (string.IsNullOrEmpty(_mainWindow._scanFilePath))
                    {
                        _mainWindow._scanFilePath = docFilePath;
                    }

                    string modName = pDoc.ModelStateName;
                    result.AddNode(ProcessPart(pDoc, modName));
                }

                _mainWindow.UpdateLog("====================\n", false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.StackTrace);
                _mainWindow.CheckExit.NeedExit = true;
                _mainWindow.CheckExit.Message = $"Ошибка при добавлении файла: {docFilePath}";
                _mainWindow.CheckExit.SystemMessage = ex.StackTrace;
                _mainWindow.CheckExit.UserName = _invApp.UserName;

                List<string> infoList = new List<string>()
                {
                    $"\tПуть до файла: {docFilePath}",
                    $"\t{ _mainWindow.CheckExit.Message}",
                    $"\tПоследние логи:\n{_mainWindow.GetLinesAsText()}"
                };
                if (DateTime.UtcNow - _lastErrorEmailSent >= _emailCooldown)
                {
                    CommonOperations.EmailOnError(ex.Message, ex.StackTrace, _invApp.UserName, infoList);
                    _lastErrorEmailSent = DateTime.UtcNow;  // фиксируем время отправки
                }

                //throw new InvalidOperationException(ex.Message,ex);
            }
            finally
            {

            }

            return result;
        }
        
        /// <summary>
        /// Метод добавления файлов
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <param name="choiceFiles">Выбранные пользователем файлы</param>
        /// <returns></returns>
        private ScanResult AddProcessing(CancellationToken cancellationToken, List<string> choiceFiles)
        {
            // Вызывает исключение, если приложение закрыли
            cancellationToken.ThrowIfCancellationRequested();

            ScanResult result = new ScanResult();
            string docFilePath = string.Empty;

            try
            {
                bool wasOpened = false;
                // Проходим по всем файлам, которые пользователь выбрал
                foreach (string filePath in choiceFiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    PartDocument pDoc = CommonOperations.GetOrOpenPartDocument(_invApp, filePath, out wasOpened);
                    if (pDoc == null)
                        throw new Exception("Не удалось получить PartDocument.");
                    
                    // Если нужно выйти
                    if (_mainWindow.CheckExit.NeedExit)
                    {
                        _mainWindow.UpdateLog(_mainWindow.CheckExit.Message, false);
                        return null;
                    }
                    
                    // пропускаем многотельную деталь
                    if (pDoc.ComponentDefinition.SurfaceBodies.Count > 1)
                    {
                        pDoc.Close(SkipSave:true);
                        _mainWindow.UpdateLog($"Пропущена деталь: {pDoc.DisplayName}");
                        continue;
                    }

                    // пропускаем не листовую деталь
                    if (pDoc.SubType != SubType.ЛистоваяДеталь)
                    {
                        pDoc.Close(SkipSave: true);
                        _mainWindow.UpdateLog($"Пропущена деталь: {pDoc.DisplayName}");
                        continue;
                    }

                    docFilePath = pDoc.FullFileName;
                    if (string.IsNullOrEmpty(_mainWindow._scanFilePath))
                    {
                        _mainWindow._scanFilePath = docFilePath;
                    }

                    string modName = pDoc.ModelStateName;
                    result.AddNode(ProcessPart(pDoc, modName));
                }

                _mainWindow.UpdateLog("====================\n", false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.StackTrace);
                _mainWindow.CheckExit.NeedExit = true;
                _mainWindow.CheckExit.Message = $"Ошибка при добавлении файла: {docFilePath}";
                _mainWindow.CheckExit.SystemMessage = ex.StackTrace;
                _mainWindow.CheckExit.UserName = _invApp.UserName;

                List<string> infoList = new List<string>()
                {
                    $"\tПуть до файла: {docFilePath}",
                    $"\t{ _mainWindow.CheckExit.Message}",
                    $"\tПоследние логи:\n{_mainWindow.GetLinesAsText()}"
                };
                if (DateTime.UtcNow - _lastErrorEmailSent >= _emailCooldown)
                {
                    CommonOperations.EmailOnError(ex.Message, ex.StackTrace, _invApp.UserName, infoList);
                    _lastErrorEmailSent = DateTime.UtcNow;  // фиксируем время отправки
                }

                //throw new InvalidOperationException(ex.Message,ex);
            }
            finally
            {

            }

            return result;
        }

        /// <summary>
        /// Проверяет, большая ли это развёртка для листа, Есть ли сочетание материал - толщина.
        /// </summary>
        /// <returns>BigFlat = true - большая, не поместится, false - обычная, поместится.
        /// errMatThick = true - нет такого сочетания материала, толщины,
        /// оба false, если ошибка</returns>
        private (bool BigFlat, bool errMatThick) CheckFlat(string material,double thickness, SheetMetalComponentDefinition sheetMetalCompDef)
        {
            if (!sheetMetalCompDef.HasFlatPattern || sheetMetalCompDef.FlatPattern?.MassProperties.Mass == 0)
            {
                return (false,false);
            }

            double flatWidth = sheetMetalCompDef.FlatPattern.Width*10;
            double flatLength = sheetMetalCompDef.FlatPattern.Length*10;

            var dataTable = ExcelDataLoader.GetInstance(CheckSettings.PathTable)
                .GetDataFromPathTable(material, thickness);

            if (dataTable.ListLength == 0 || dataTable.ListWidth == 0) return (false,true);

            bool fits =
                !((flatLength <= dataTable.ListLength && flatWidth <= dataTable.ListWidth) ||
                  (flatLength <= dataTable.ListWidth && flatWidth <= dataTable.ListLength));
            
            return (fits,false);
        }

        /// <summary>
        /// Получение реальной толщины из детали
        /// </summary>
        /// <param name="sheetMetalCompDef"></param>
        /// <returns>Толщина из детали</returns>
        private double GetThickFromPart(SheetMetalComponentDefinition sheetMetalCompDef)
        {
            if (!sheetMetalCompDef.HasFlatPattern || sheetMetalCompDef.FlatPattern?.MassProperties.Mass == 0)
            {
                return 0;
            }

            double thick = Math.Min(sheetMetalCompDef.FlatPattern.OrientedMinimumRangeBox.DirectionOne.Length,
                sheetMetalCompDef.FlatPattern.OrientedMinimumRangeBox.DirectionTwo.Length);

            thick = Math.Min(thick, sheetMetalCompDef.FlatPattern.OrientedMinimumRangeBox.DirectionThree.Length);

            return thick;
        }
    }
}
