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

namespace DxfExporter.Scanning
{
    internal class ScanningProcess
    {
        private readonly MainWindow _mainWindow;
        private Inventor.Application _invApp = null;
        private double _percent;

        //для ограничения отправки писем
        private static DateTime _lastErrorEmailSent = DateTime.MinValue;
        private static readonly TimeSpan _emailCooldown = TimeSpan.FromMinutes(5);

        //public StateWithMessage CheckExit = new StateWithMessage { };

        /// <summary>
        /// Для обработки потока
        /// </summary>
        //private ManualResetEvent _completionEvent = new ManualResetEvent(false);

        public ScanningProcess(MainWindow mainWindow)
        {
            _mainWindow = mainWindow;
        }
        
        /// <summary>
        /// Точка входа в логику обработки, разделение потоков
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<ScanResult> StartProcessingAsync(CancellationToken cancellationToken)
        {

            // Выполняем обработку в STA-потоке Inventor через InventorHost
            return await InventorHost.Instance.Value.RunAsync(async invApp =>
            {
                _invApp = invApp;

                // логика работы с Inventor
                var result = DataProcessing(cancellationToken);

                //MessageBox.Show("Test");

                if (_mainWindow.CheckExit.NeedExit)
                {
                    _mainWindow.Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show(_mainWindow.CheckExit.Message, "Сканирование не завершено!",
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
                        if (HasMissingReferences((AssemblyDocument)doc))
                        {
                            _mainWindow.CheckExit.NeedExit = true;
                            _mainWindow.CheckExit.Message = "В сборке есть потерянные ссылки. Необходимо скорректировать сборку.";
                            return null;
                        }

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
                        
                        foreach (OccStructure occStructure in occResult.ScannedOcc)
                        {
                            // Вызывает исключение, если приложение закрыли
                            cancellationToken.ThrowIfCancellationRequested();

                            PartDocument pDoc = occStructure.Occurrence.Definition.Document;
                            modName = occStructure.Occurrence.ActiveModelState;
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
            try
            {
                foreach (ComponentOccurrence componentOccurrence in asmDocument.ComponentDefinition.Occurrences)
                {
                    // Вызывает исключение, если приложение закрыли
                    cancellationToken.ThrowIfCancellationRequested();
                    // Выход, если приложение сигнализирует о завершении
                    if (_mainWindow.CheckExit.NeedExit) return;

                    _mainWindow.UpdateOverlay(CommonConstants.OverlayProcessScan, componentOccurrence.Name);

                    // пропускает исключенные или подавленные элементы
                    if (componentOccurrence.Suppressed || componentOccurrence.Excluded)
                    {
                        
                        _mainWindow.UpdateLog($"Пропущена деталь: {componentOccurrence.Name}");
                        _mainWindow.MinusProgress(_percent);
                        continue; 
                    }

                    // пропустить стандартные изделия
                    if (componentOccurrence.BOMStructure is BOMStructureEnum.kPurchasedBOMStructure)
                    {
                        _mainWindow.UpdateLog($"Пропущена деталь: {componentOccurrence.Name}");
                        _mainWindow.MinusProgress(_percent);
                        continue;
                    }

                    // пропустить детали из Content center
                    if (componentOccurrence.Definition is PartComponentDefinition def && def.IsContentMember)
                    {
                        _mainWindow.UpdateLog($"Пропущена деталь: {componentOccurrence.Name}");
                        _mainWindow.MinusProgress(_percent);
                        continue;
                    }

                    // проверяем сборка или деталь
                    if (componentOccurrence.DefinitionDocumentType == DocumentTypeEnum.kAssemblyDocumentObject)
                    {
                        AssemblyDocument asmDoc = componentOccurrence.Definition.Document;
                        // пропускаем сборку если в ней нет деталей
                        if (asmDoc.ComponentDefinition.Occurrences.Count == 0)
                        {
                            _mainWindow.UpdateLog($"Пропущена сборка: {componentOccurrence.Name}");
                            _mainWindow.MinusProgress(_percent);
                            continue;
                        }

                        ScanOccurrences(cancellationToken, asmDoc, occResult);

                        _mainWindow.UpdateLog($"Обработана сборка: {componentOccurrence.Name}");
                        _mainWindow.MinusProgress(_percent);
                    }
                    else if (componentOccurrence.DefinitionDocumentType == DocumentTypeEnum.kPartDocumentObject)
                    {
                        PartDocument pDoc = (PartDocument)componentOccurrence.Definition.Document;
                        // пропускаем многотельную деталь
                        if (pDoc.ComponentDefinition.SurfaceBodies.Count > 1)
                        {
                            _mainWindow.UpdateLog($"Пропущена деталь: {componentOccurrence.Name}");
                            _mainWindow.MinusProgress(_percent);
                            continue;
                        }

                        // пропускаем не листовую деталь
                        if (pDoc.SubType != SubType.ЛистоваяДеталь)
                        {
                            _mainWindow.UpdateLog($"Пропущена деталь: {componentOccurrence.Name}");
                            _mainWindow.MinusProgress(_percent);
                            continue;
                        }
                        
                        //Создание структуры
                        OccStructure structure = new OccStructure()
                        {
                            Path = componentOccurrence.ReferencedDocumentDescriptor.FullDocumentName,
                            Quantity = 1,
                            Occurrence = componentOccurrence
                        };

                        occResult.AddNode(structure);

                        _mainWindow.UpdateLog($"Обработана деталь: {componentOccurrence.Name}");
                        _mainWindow.MinusProgress(_percent);
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
        private StructureClass ProcessPart(PartDocument partDoc, string modName, double quantity = 1)
        {
            SheetMetalComponentDefinition sheetMetalCompDef = (SheetMetalComponentDefinition)partDoc.ComponentDefinition;

            StructureClass structure = CreateStructure(partDoc, sheetMetalCompDef, modName, quantity);

            var memberNames = GetMemberNames(sheetMetalCompDef);
            if (memberNames.Count > 0)
            {
                structure.IsGroup = true;
                structure.NeedUnload = false;
                structure.NeedGrav = false;
                structure.NeedBendLine = false;
                structure.UnloadInTemplate = false;
                structure.MemberName = string.Empty;
                structure.UpdateUnloadProp();

                foreach (string member in memberNames)
                {
                    StructureClass child = CreateStructure(partDoc, sheetMetalCompDef, member, quantity);
                    child.IsGroup = false;
                    structure.Children.Add(child);
                }
            }
            
            _mainWindow.UpdateLog($"Обработана деталь: {partDoc.DisplayName}");

            return structure;
        }

        private StructureClass CreateStructure(PartDocument partDoc, SheetMetalComponentDefinition sheetMetalCompDef, string modName, double quantity)
        {
            string partNumber = (string)GetPropertyValue(partDoc.PropertySets, "Design Tracking Properties",
                "Part Number", "N/A");
            string description = (string)GetPropertyValue(partDoc.PropertySets, "Design Tracking Properties",
                "Description", "N/A");
            string material = sheetMetalCompDef.Material.Name;
            double thick = Math.Round((double)sheetMetalCompDef.Thickness.Value * 10, 2);

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
                NullFlat = sheetMetalCompDef.FlatPattern?.MassProperties.Mass == 0
            };

            return structure;
        }

        private static List<string> GetMemberNames(SheetMetalComponentDefinition sheetMetalCompDef)
        {
            HashSet<string> names = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);

            if (sheetMetalCompDef.IsModelStateFactory || sheetMetalCompDef.IsModelStateMember)
            {
                try
                {
                    foreach (ModelState state in sheetMetalCompDef.ModelStates)
                    {
                        if (!string.IsNullOrWhiteSpace(state.Name))
                        {
                            names.Add(state.Name);
                        }
                    }
                }
                catch
                {
                }
            }

            if (sheetMetalCompDef.IsiPartFactory || sheetMetalCompDef.IsiPartMember)
            {
                try
                {
                    var factory = sheetMetalCompDef.iPartFactory;
                    if (factory == null && sheetMetalCompDef.IsiPartMember)
                    {
                        factory = sheetMetalCompDef.iPartMember?.iPartFactory;
                    }

                    if (factory != null)
                    {
                        foreach (iPartTableRow row in factory.TableRows)
                        {
                            string memberName = row.MemberName;
                            if (!string.IsNullOrWhiteSpace(memberName))
                            {
                                names.Add(memberName);
                            }
                        }
                    }
                }
                catch
                {
                }
            }

            return names.OrderBy(name => name).ToList();
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

        
    }
}
