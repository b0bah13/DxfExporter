using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.JavaScript;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DxfExporter.Constants;
using DxfExporter.Export_Dxf;
using DxfExporter.MaskProcess;
using DxfExporter.Scanning;
using DxfExporter.Versions;
using Inventor;
using static System.Net.Mime.MediaTypeNames;
using Environment = System.Environment;
using Point = System.Windows.Point;

namespace DxfExporter
{
    public partial class MainWindow : Window
    {
        #region Объявление переменных

        private CancellationTokenSource _cts;
        public StateWithMessage CheckExit = new StateWithMessage { };
        private ScanResult _scanResult;
        private readonly InventorHost _host = InventorHost.Instance.Value;
        public string _scanFilePath;
        public MaskEditorViewModel MaskVm { get; } = new MaskEditorViewModel();
        public ExportSettings FolderSettings = new ExportSettings();
        public string AppVersion { get; private set; }
        private readonly List<TourStep> _tourSteps = new();
        private int _tourIndex;
        private bool _isTourActive;

        /// <summary>
        /// Перечисление для указания источника вызова. 
        /// </summary>
        public enum CallSource
        {
            Scan,
            Export
        }

        public class ExportSettings
        {
            public bool SubFolderMaterials { get; set; } = true;
            public bool SubFolderThickness { get; set; } = true;
            public bool UserDxfDir { get; set; } = false;
        }


        #endregion


        public MainWindow()
        {
            InitializeComponent();

            //DataContext = this;
            DataContext = MaskVm; 
            AppVersion = GetAppVersion();   // или просто присвоить строку

            UpdateTooltip();

            // для обработки закрытия формы в другом потоке
            _cts = new CancellationTokenSource();
            
            // отображение окна с информацией об изменениях
            if (ShowNewVersion.CheckNeedShow())
            {
                var lastVer = Versions.VersionHistory.GetLatest();

                string txtInfo = lastVer.Description;
                ShowNewVersion.ShowInfo(txtInfo);
            }

            Loaded += MainWindow_Loaded;
            SizeChanged += MainWindow_SizeChanged;
        }

        #region Обработка кастомного заголовка

        /// <summary>
        /// Для обработки кастомного заголовка
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void DragWindow(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                if (e.ClickCount == 2)
                {
                    Maximize_Click(sender, e);
                    return;
                }

                DragMove();
            }
        }

        /// <summary>
        /// Для закрытия кастомного заголовка
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        //private void CloseClick(object sender, RoutedEventArgs e) => Close();
        private void CloseClick(object sender, RoutedEventArgs e)
        {
            // Отменяем операцию при закрытии окна
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;

            if (InventorHost.Instance.IsValueCreated && InventorHost.Instance.Value.IsInitialized)
            {
                InventorHost.Instance.Value.Dispose(); // Вызываем Dispose только если Inventor был инициализирован
            }

            Close();
        }

        /// <summary>
        /// Свернуть
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        /// <summary>
        /// Развернуть
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Maximize_Click(object sender, RoutedEventArgs e)
        {
            switch (WindowState)
            {
                case WindowState.Maximized:
                    MaximizeButton.Content = "⬜";
                    MaximizeButton.ToolTip = "Развернуть";
                    WindowState = WindowState.Normal;
                    break;
                case WindowState.Normal:
                    MaximizeButton.Content = "❐";
                    MaximizeButton.ToolTip = "Свернуть в окно";
                    WindowState = WindowState.Maximized;
                    break;
            }

        }

        /// <summary>
        /// Окно с информацией о программе
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void InfoClick(object sender, RoutedEventArgs e)
        {
            StartTour();
        }
        
        #endregion

        #region Обработчик кнопок, нажатий клавиш
        
        /// <summary>
        /// Кнопка сканирования
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void scanButton_Click(object sender, RoutedEventArgs e)
        {
            StartScanProcess();
        }

        /// <summary>
        /// Кнопка создания dxf
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void startButton_Click(object sender, RoutedEventArgs e)
        {
            StartCreateDxfProcess();
        }

        /// <summary>
        /// Очистка поля поиска
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void clearButton_Click(object sender, RoutedEventArgs e)
        {
            searchBox.Clear();
        }

        /// <summary>
        /// Обработка ввода текста в поле поиска.
        /// Выполняет фильтрацию данных в активной вкладке по выбранному полю.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            SearchProc();
        }

        /// <summary>
        /// Произошло изменение заголовка поиска
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void searchComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            SearchProc();
        }

        /// <summary>
        /// Обработка чекбоксов выгрузки подпапок
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void categorizeFolder_Checked(object sender, RoutedEventArgs e)
        {
            var checkBox = sender as CheckBox;

            switch (checkBox.Name)
            {
                case "categorizeMaterial":
                    FolderSettings.SubFolderMaterials = categorizeMaterial.IsChecked == true;
                    break;
                case "categorizeThickness":
                    FolderSettings.SubFolderThickness = categorizeThickness.IsChecked == true;
                    break;
                case "userDirect":
                    FolderSettings.UserDxfDir = userDirect.IsChecked == true;
                    break;
            }

            UpdateTooltip();
        }
        
        /// <summary>
        /// Окно с информацией о выпущенных версиях
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void VersInfoClick(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(Versions.VersionHistory.GetFullHistory(),
                "Информация об обновлениях", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        #region Обработка выборка маски выгрузки

        /// <summary>
        /// Произошло изменение маски выгрузки
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void templateComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (templateComboBox.SelectedItem == null) return;

            string template = templateComboBox.SelectedItem.ToString();
            MaskVm.ApplyTemplate(template);

            //if (gbMaskSettings != null) gbMaskSettings.IsEnabled = MaskVm.IsMaskSettingsEnabled;
            if (gridSettings != null) gridSettings.IsEnabled = MaskVm.IsMaskSettingsEnabled;

        }

        /// <summary>
        /// Кнопка добавить из левого списка
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void AddFromLeft_Click(object sender, RoutedEventArgs e)
        {
            if (AvailableList.SelectedItem is HeaderItem item)
            {
                MaskVm.AddHeaderToMask(item);
            }
        }

        private void AvailableList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (AvailableList.SelectedItem is HeaderItem item)
            {
                MaskVm.AddHeaderToMask(item);
            }
        }
        
        /// <summary>
        /// Кнопка добавить пользовательский текст
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void AddCustomText_Click(object sender, RoutedEventArgs e)  // если не используешь Command
        {
            
            MaskVm.AddCustomTextToMask();
            txtCustomText.Text = string.Empty;
        }

        /// <summary>
        /// Двойной клик в правом списке
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SelectedList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (SelectedList.SelectedItem is MaskPart part)
                MaskVm.RemovePart(part);
        }

        /// <summary>
        /// Кнопка переместить вверх
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MoveUp_Click(object sender, RoutedEventArgs e)
        {
            int idx = SelectedList.SelectedIndex;
            MaskVm.MoveUp(idx);
            SelectedList.SelectedIndex = Math.Max(0, idx - 1);
        }

        /// <summary>
        /// Кнопка переместить вниз
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MoveDown_Click(object sender, RoutedEventArgs e)
        {
            int idx = SelectedList.SelectedIndex;
            MaskVm.MoveDown(idx);
            SelectedList.SelectedIndex = Math.Min(MaskVm.MaskParts.Count - 1, idx + 1);
        }

        /// <summary>
        /// Удаляет выбранный элемент из правого списка (SelectedList)
        /// </summary>
        private void RemoveSelected_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedList.SelectedItem is MaskPart part)
                MaskVm.RemovePart(part);
        }
        
        /// <summary>
        /// Полностью очищает правый список (SelectedList)
        /// </summary>
        private void ClearMaskParts_Click(object sender, RoutedEventArgs e)
        {
            MaskVm.ClearMaskParts();
        }

        #region Drag & Drop — левый список (добавление в маску)

        /// <summary>
        /// Захват начала перетаскивания из левого списка
        /// </summary>
        private void AvailableList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                MaskVm.RememberDragStart(e.GetPosition(null));
        }

        /// <summary>
        /// Начало перетаскивания из левого списка → создаём объект и запоминаем исходный HeaderItem
        /// </summary>
        private void AvailableList_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;

            Point pos = e.GetPosition(null);
            if (!MaskVm.CanStartDrag(pos)) return;

            if (AvailableList.SelectedItem is HeaderItem sourceItem)
            {
                var dragData = MaskVm.CreateDragDataFromHeader(sourceItem);
                if (dragData != null)
                    DragDrop.DoDragDrop(AvailableList, dragData, DragDropEffects.Copy);
            }
        }

        private void AvailableList_Drop(object sender, DragEventArgs e)
        {
            // Перетаскивание обратно из правого → снимаем флаг IsUsed
            if (e.Data.GetDataPresent(typeof(MaskPart)))
            {
                if (e.Data.GetData(typeof(MaskPart)) is MaskPart droppedPart)
                    MaskVm.DropToAvailableList(droppedPart);
            }
        }

        #endregion

        #region Drag & Drop — правый список (перемещение внутри + удаление обратно)

        private void SelectedList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                MaskVm.RememberDragStart(e.GetPosition(null));
        }

        private void SelectedList_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;

            Point pos = e.GetPosition(null);
            if (!MaskVm.CanStartDrag(pos)) return;

            if (SelectedList.SelectedItem is MaskPart part)
                DragDrop.DoDragDrop(SelectedList, part, DragDropEffects.Move);
        }

        /// <summary>
        /// Drop в правый список — обрабатываем как из левого, так и внутри правого
        /// </summary>
        private void SelectedList_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(typeof(Tuple<MaskPart, HeaderItem>)) &&
                !e.Data.GetDataPresent(typeof(MaskPart)))
                return;

            MaskPart dropped = null;
            HeaderItem sourceHeader = null;
            bool moveInside = false;

            if (e.Data.GetDataPresent(typeof(Tuple<MaskPart, HeaderItem>)))
            {
                var tuple = e.Data.GetData(typeof(Tuple<MaskPart, HeaderItem>)) as Tuple<MaskPart, HeaderItem>;
                if (tuple != null)
                {
                    dropped = tuple.Item1;
                    sourceHeader = tuple.Item2;
                }
            }
            else if (e.Data.GetDataPresent(typeof(MaskPart)))
            {
                dropped = e.Data.GetData(typeof(MaskPart)) as MaskPart;
                moveInside = true;
            }

            if (dropped == null) return;

            int insertIndex = -1;
            var hit = SelectedList.InputHitTest(e.GetPosition(SelectedList)) as FrameworkElement;

            if (hit?.DataContext is MaskPart target)
            {
                insertIndex = MaskVm.MaskParts.IndexOf(target);
                if (e.GetPosition(hit).Y > hit.ActualHeight / 2)
                    insertIndex++;
            }

            MaskVm.DropToSelectedList(dropped, sourceHeader, insertIndex, moveInside);
        }

        #endregion

        #endregion

        #region Обработка контекстного меню
        /// <summary>
        /// Обработка нажатия замены материала в контекстном меню
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MenuItem_ChangeMaterial_Click(object sender, RoutedEventArgs e)
        {
            // Получаем выбранные строки
            var selected = scanData.SelectedItems
                .OfType<StructureClass>()
                .ToList();
            if (!selected.Any()) return;

            ChangeMaterial(selected);
        }

        /// <summary>
        /// Обработка нажатия замены материала в контекстном меню
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MenuItem_AddMaterial_Click(object sender, RoutedEventArgs e)
        {
            // Получаем выбранные строки
            var selected = scanData.SelectedItems
                .OfType<StructureClass>()
                .ToList();
            if (!selected.Any()) return;

            AddMaterial(selected);
        }

        /// <summary>
        /// Обработка включения чекбокса в контекстном меню
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Menu_Checked_General(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem menuItem) return;
            string propertyName = menuItem.Tag?.ToString();
            if (string.IsNullOrEmpty(propertyName)) return;

            var selectedRows = scanData.SelectedItems.OfType<StructureClass>().ToList();
            if (!selectedRows.Any()) return;

            foreach (var row in selectedRows)
            {
                switch (propertyName)
                {
                    case "NeedUnload":
                        row.NeedUnload = true;
                        break;
                    case "NeedGrav":
                        row.NeedGrav = true;
                        break;

                    case "NeedBendLine":
                        row.NeedBendLine = true;
                        break;

                    case "UnloadInTemplate":
                        row.UnloadInTemplate = true;
                        break;

                    case "UnloadAllVers":
                        // Можно дополнительно проверять, но т.к. IsEnabled уже фильтрует
                        row.UnloadAllVers = true;
                        break;
                }
                row.UpdateUnloadProp();
            }
        }

        /// <summary>
        /// Обработка выключения чекбокса в контекстном меню
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Menu_Unchecked_General(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem menuItem) return;
            string propertyName = menuItem.Tag?.ToString();
            if (string.IsNullOrEmpty(propertyName)) return;

            var selectedRows = scanData.SelectedItems.OfType<StructureClass>().ToList();
            if (!selectedRows.Any()) return;

            foreach (var row in selectedRows)
            {
                switch (propertyName)
                {
                    case "NeedUnload":
                        row.NeedUnload = false;
                        break;
                    case "NeedGrav":
                        row.NeedGrav = false;
                        break;

                    case "NeedBendLine":
                        row.NeedBendLine = false;
                        break;

                    case "UnloadInTemplate":
                        row.UnloadInTemplate = false;
                        break;

                    case "UnloadAllVers":
                        row.UnloadAllVers = false;
                        break;
                }
                row.UpdateUnloadProp();
            }
        }

        /// <summary>
        /// Отменяем показ контекстного меню, если нет выбранных строк
        /// </summary>
        private void scanData_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (scanData.SelectedItems.Count == 0)
            {
                e.Handled = true;           // ← это ключевое — отменяет открытие меню
                return;
            }

            // Дополнительная проверка — меню только при клике по строке
            var pos = Mouse.GetPosition(scanData);
            var hit = VisualTreeHelper.HitTest(scanData, pos);

            if (hit?.VisualHit == null || !IsVisualChildOfDataGridRow(hit.VisualHit))
            {
                e.Handled = true;
            }
            
        }

        /// <summary>
        /// Проверяет, является ли визуальный элемент частью DataGridRow
        /// </summary>
        private bool IsVisualChildOfDataGridRow(DependencyObject element)
        {
            while (element != null)
            {
                if (element is DataGridRow)
                    return true;
                element = VisualTreeHelper.GetParent(element);
            }
            return false;
        }

        /// <summary>
        /// Срабатывает каждый раз при открытии контекстного меню
        /// Здесь мы синхронизируем состояние чекбоксов с первой выбранной строкой
        /// </summary>
        private void ContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            if (sender is not ContextMenu menu) return;
            if (scanData.SelectedItems.Count == 0) return;

            var first = scanData.SelectedItems[0] as StructureClass;
            if (first == null) return;

            foreach (MenuItem item in menu.Items.OfType<MenuItem>())
            {
                if (item.Tag == null) continue;

                string tag = item.Tag.ToString();

                switch (tag)
                {
                    case "NeedUnload":
                        item.IsChecked = first.NeedUnload;
                        break;
                    case "NeedGrav":
                        item.IsChecked = first.NeedGrav;
                        break;
                    case "NeedBendLine":
                        item.IsChecked = first.NeedBendLine;
                        break;
                    case "UnloadInTemplate":
                        item.IsChecked = first.UnloadInTemplate;
                        break;
                    case "UnloadAllVers":
                    {
                        bool can = first.IsIPart || first.IsModelStatePart;
                        item.IsEnabled = can;
                        item.IsChecked = can && first.UnloadAllVers;
                        break;
                    }
                }
            }
        }

        #endregion

        #region Ознакомительный режим

        private sealed class TourStep
        {
            public TabItem Tab { get; init; }
            public FrameworkElement Target { get; init; }
            public string Title { get; init; }
            public string Description { get; init; }
        }

        /// <summary>
        /// Инициализирует список шагов ознакомительного режима после загрузки окна.
        /// </summary>
        /// <param name="sender">Источник события.</param>
        /// <param name="e">Аргументы события.</param>
        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            InitializeTourSteps();
        }

        /// <summary>
        /// Пересчитывает позицию подсветки при изменении размеров окна.
        /// </summary>
        /// <param name="sender">Источник события.</param>
        /// <param name="e">Аргументы события.</param>
        private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_isTourActive)
            {
                UpdateTourVisual();
            }
        }

        /// <summary>
        /// Формирует последовательность шагов для ознакомительного режима.
        /// </summary>
        private void InitializeTourSteps()
        {
            // Очищаем предыдущую последовательность шагов.
            _tourSteps.Clear();
            _tourSteps.Add(new TourStep
            {
                Tab = HomeTab,
                Target = scanButton,
                Title = "Сканирование",
                Description = "Запускает сканирование выбранной сборки или детали."
            });
            _tourSteps.Add(new TourStep
            {
                Tab = HomeTab,
                Target = startButton,
                Title = "Создание DXF",
                Description = "Создаёт DXF для выбранных позиций в таблице."
            });
            _tourSteps.Add(new TourStep
            {
                Tab = HomeTab,
                Target = searchBox,
                Title = "Поиск",
                Description = "Введите текст, чтобы отфильтровать таблицу по выбранному столбцу."
            });
            _tourSteps.Add(new TourStep
            {
                Tab = HomeTab,
                Target = searchComboBox,
                Title = "Столбец поиска",
                Description = "Выберите, по какому столбцу искать совпадения."
            });
            _tourSteps.Add(new TourStep
            {
                Tab = HomeTab,
                Target = clearButton,
                Title = "Очистка",
                Description = "Сбрасывает строку поиска и возвращает полный список."
            });
            _tourSteps.Add(new TourStep
            {
                Tab = HomeTab,
                Target = scanData,
                //Title = "Таблица с данными",
                Title = "Список отсканированных деталей",
                Description = "При нажатии ПКМ открывается контекстное меню, где можно выбрать свойства выгрузки.\n" +
                              "Описание значений в столбце \"Свойства выгрузки\":\n" +
                              "Грав. - выгружается гравировка\n" +
                              "Гиб - выгружаются линии гиба\n" +
                              "Шаблон - DXF файлы будут выгружены в папку \"Шаблон\""
            });
            _tourSteps.Add(new TourStep
            {
                Tab = SettingsTab,
                Target = userDirect,
                Title = "Папка выгрузки",
                Description = "Включает пользовательское расположение папки для DXF файлов."
            });
            _tourSteps.Add(new TourStep
            {
                Tab = SettingsTab,
                Target = categorizeMaterial,
                Title = "Папка выгрузки",
                Description = "Включает распределение DXF файлов по материалам."
            });
            _tourSteps.Add(new TourStep
            {
                Tab = SettingsTab,
                Target = categorizeThickness,
                Title = "Папка выгрузки",
                Description = "Включает распределение DXF файлов по толщинам."
            });
            _tourSteps.Add(new TourStep
            {
                Tab = SettingsTab,
                Target = templateComboBox,
                Title = "Шаблон имени",
                Description = "Выберите шаблон имени файла DXF или настройте собственный."
            });
            _tourSteps.Add(new TourStep
            {
                Tab = SettingsTab,
                Target = gbMaskSettings,
                Title = "Состав шаблона",
                Description = "Перетаскивайте параметры и настраивайте порядок частей имени."
            });

            // _tourSteps.Add(new TourStep { Tab = HomeTab, Target = <имя_элемента>, Title = "<заголовок>", Description = "<описание>" });
            // _tourSteps.Add(new TourStep { Tab = SettingsTab, Target = <имя_элемента>, Title = "<заголовок>", Description = "<описание>" });
        }

        /// <summary>
        /// Запускает ознакомительный режим с первого шага.
        /// </summary>
        private void StartTour()
        {
            if (_tourSteps.Count == 0)
            {
                // Если шаги ещё не подготовлены, создаём их.
                InitializeTourSteps();
            }

            if (_tourSteps.Count == 0)
            {
                // Нечего показывать.
                return;
            }

            // Сбрасываем индекс на первый шаг и включаем оверлей.
            _tourIndex = 0;
            _isTourActive = true;
            TourOverlay.Visibility = Visibility.Visible;
            // Отрисовываем первый шаг.
            ShowTourStep();
        }

        /// <summary>
        /// Завершает ознакомительный режим и скрывает оверлей.
        /// </summary>
        private void EndTour()
        {
            // Сбрасываем флаг активности и скрываем оверлей.
            _isTourActive = false;
            TourOverlay.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// Отображает текущий шаг и запускает пересчёт позиции подсветки.
        /// </summary>
        private void ShowTourStep()
        {
            if (_tourIndex < 0 || _tourIndex >= _tourSteps.Count)
            {
                // Если индекс вышел за границы — завершаем тур.
                EndTour();
                return;
            }

            var step = _tourSteps[_tourIndex];
            if (step.Tab != null)
            {
                // Переключаем вкладку на нужную.
                MainTabControl.SelectedItem = step.Tab;
            }

            // Обновляем тексты и состояние кнопок.
            TourTitleText.Text = step.Title;
            TourDescriptionText.Text = step.Description;
            TourPrevButton.IsEnabled = _tourIndex > 0;
            TourNextButton.Content = _tourIndex >= _tourSteps.Count - 1 ? "Завершить" : "Вперёд";

            // После изменения вкладки пересчитываем позицию элементов.
            Dispatcher.BeginInvoke(UpdateTourVisual, DispatcherPriority.Loaded);
        }

        /// <summary>
        /// Вычисляет геометрию затемнения и позицию подсказки для текущего элемента.
        /// </summary>
        private void UpdateTourVisual()
        {
            if (!_isTourActive || _tourIndex < 0 || _tourIndex >= _tourSteps.Count)
            {
                // Не пересчитываем позицию, если тур неактивен.
                return;
            }

            var target = _tourSteps[_tourIndex].Target;
            if (target == null)
            {
                // Если элемента нет, нечего подсвечивать.
                return;
            }

            target.UpdateLayout();
            if (target.ActualWidth <= 0 || target.ActualHeight <= 0)
            {
                // Повторяем пересчёт, когда элемент получит размеры.
                Dispatcher.BeginInvoke(UpdateTourVisual, DispatcherPriority.Loaded);
                return;
            }

            // Считаем координаты относительно оверлея, чтобы избежать смещения из-за рамки окна.
            var overlayRect = new Rect(0, 0, TourOverlay.ActualWidth, TourOverlay.ActualHeight);
            // Преобразуем координаты элемента через окно, чтобы не зависеть от родителя оверлея.
            var targetInWindow = target.TransformToAncestor(this)
                .TransformBounds(new Rect(0, 0, target.ActualWidth, target.ActualHeight));
            var overlayOffsetInWindow = TourOverlay.TransformToAncestor(this)
                .Transform(new Point(0, 0));
            var targetRect = new Rect(
                targetInWindow.Left - overlayOffsetInWindow.X,
                targetInWindow.Top - overlayOffsetInWindow.Y,
                targetInWindow.Width,
                targetInWindow.Height);
            targetRect.Inflate(6, 6);

            var overlayGeometry = new GeometryGroup { FillRule = FillRule.EvenOdd };
            overlayGeometry.Children.Add(new RectangleGeometry(overlayRect));
            overlayGeometry.Children.Add(new RectangleGeometry(targetRect, 6, 6));
            TourDimPath.Data = overlayGeometry;

            TourHighlightBorder.Width = targetRect.Width;
            TourHighlightBorder.Height = targetRect.Height;
            Canvas.SetLeft(TourHighlightBorder, targetRect.Left);
            Canvas.SetTop(TourHighlightBorder, targetRect.Top);

            // Ширина области с описанием
            TourDescriptionText.Width = target.Name == "scanData" ? 400 : 260;

            // Подбираем позицию подсказки рядом с элементом и удерживаем её в пределах оверлея.
            TourTooltipBorder.Measure(new Size(300, double.PositiveInfinity));
            var tooltipSize = TourTooltipBorder.DesiredSize;
            var tooltipLeft = targetRect.Right + 12;
            if (tooltipLeft + tooltipSize.Width > overlayRect.Right)
            {
                tooltipLeft = targetRect.Left - tooltipSize.Width - 12;
            }

            if (tooltipLeft < 12)
            {
                tooltipLeft = 12;
            }

            var tooltipTop = targetRect.Top;
            if (tooltipTop + tooltipSize.Height > overlayRect.Bottom)
            {
                tooltipTop = overlayRect.Bottom - tooltipSize.Height - 12;
            }

            if (tooltipTop < 12)
            {
                tooltipTop = 12;
            }

            Canvas.SetLeft(TourTooltipBorder, tooltipLeft);
            Canvas.SetTop(TourTooltipBorder, tooltipTop);
        }

        /// <summary>
        /// Переходит к предыдущему шагу тура.
        /// </summary>
        private void TourPrev_Click(object sender, RoutedEventArgs e)
        {
            if (_tourIndex <= 0)
            {
                // Первый шаг — назад недоступен.
                return;
            }

            _tourIndex--;
            ShowTourStep();
        }

        /// <summary>
        /// Переходит к следующему шагу тура или завершает его на последнем шаге.
        /// </summary>
        private void TourNext_Click(object sender, RoutedEventArgs e)
        {
            if (_tourIndex >= _tourSteps.Count - 1)
            {
                // На последнем шаге завершаем тур.
                EndTour();
                return;
            }

            _tourIndex++;
            ShowTourStep();
        }

        /// <summary>
        /// Завершает ознакомительный режим по нажатию кнопки закрытия.
        /// </summary>
        private void TourClose_Click(object sender, RoutedEventArgs e)
        {
            // Закрываем ознакомительный режим по запросу пользователя.
            EndTour();
        }

        #endregion
        
        #endregion

        #region Обработка внутренней логики

        /// <summary>
        /// Начало сканирования.
        /// </summary>
        private async void StartScanProcess()
        {
            try
            {
                // Подготовка перед сканированием
                PreparationProcess(CallSource.Scan);

                // Дать UI возможность обновить интерфейс
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);

                ScanningProcess _processor = new ScanningProcess(this);
                _scanResult = await _processor.StartProcessingAsync(_cts.Token); // Передаем токен отмены

                if (CheckExit.NeedExit)
                {
                    txtStatus.Text = CheckExit.Message;
                    return;
                }
                // Перевод результатов сканирования в список для вывода
                if (_scanResult == null) { return; }

                txtStatus.Text = $"Сканирование завершено";
                
                scanData.ItemsSource = _scanResult.ScannedData;

                foreach (StructureClass structureClass in scanData.ItemsSource)
                {
                    structureClass.UpdateUnloadProp();
                }

                // подсветить детали с ошибками
                SelectDetails(scanData.ItemsSource as ObservableCollection<StructureClass>);

            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.StackTrace);
                txtStatus.Text = $"Ошибка при сканировании";
            }
            finally
            {
                AfterProcess(CallSource.Scan);
            }
        }

        /// <summary>
        /// Начало выгрузки Dxf
        /// </summary>
        private async void StartCreateDxfProcess()
        {
            try
            {
                if (scanData.ItemsSource == null)
                {
                    MessageBox.Show("Нет деталей для выгрузки!", "Нет данных", MessageBoxButton.OK,
                        MessageBoxImage.Stop);
                    return;
                }

                if (SelectedList.ItemsSource == null || (SelectedList.ItemsSource as ObservableCollection<MaskPart>).Count == 0)
                {
                    MessageBox.Show("Не выбрана маска для выгрузки dxf файлов!\nПроверьте настройки приложения.", "Нет данных", MessageBoxButton.OK,
                        MessageBoxImage.Stop);
                    return;
                }

                int unloadCount = (scanData.ItemsSource as ObservableCollection<StructureClass>).Count(structureClass => structureClass.NeedUnload);
                if (unloadCount == 0)
                {
                    MessageBox.Show("Не выбрана ни одна деталь для выгрузки!", "Нет данных", MessageBoxButton.OK,
                        MessageBoxImage.Stop);
                    return;
                }
                
                // Подготовка перед выгрузкой
                PreparationProcess(CallSource.Export);

                // Дать UI возможность обновить интерфейс
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);

                var procData = scanData.ItemsSource as ObservableCollection<StructureClass>;
                var maskData = MaskVm.MaskParts;
                var modeName = MaskVm.ModeName;


                ExportProcess _export = new ExportProcess(this);
                string exportDir = await _export.StartProcessExport(_cts.Token, procData, maskData, modeName,
                    _scanFilePath, FolderSettings, unloadCount);

                if (CheckExit.NeedExit)
                {
                    txtStatus.Text = CheckExit.Message;
                    return;
                }

                txtStatus.Text = "Выгрузка Dxf завершена";

                MessageBoxResult userResult = MessageBoxResult.None;
                Dispatcher.Invoke(() =>
                {
                    userResult = MessageBox.Show(this,
                        $"Выгрузка Dxf успешно завершена.\nОткрыть папку?",
                        "Готово",
                        MessageBoxButton.YesNo, MessageBoxImage.Question);
                });

                if (userResult == MessageBoxResult.Yes)
                {
                    //Process.Start("explorer.exe", exportDir);
                    // Запуск explorer.exe с путём в кавычках для обработки пробелов и спецсимволов.
                    Process.Start("explorer.exe", $"\"{exportDir}\"");
                }

            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.StackTrace);
                txtStatus.Text = $"Ошибка при выгрузке dxf";
            }
            finally
            {
                AfterProcess(CallSource.Export);
            }
        }

        /// <summary>
        /// Метод подготовки перед экспортом
        /// </summary>
        private void PreparationProcess(CallSource source)
        {
            switch (source)
            {
                case CallSource.Scan:
                    LogTextBox.Clear();
                    scanData.ItemsSource = null;

                    OverlayText.Text = CommonConstants.OverlayScan;
                    txtStatus.Text = "Сканирование файла...";
                    break;
                case CallSource.Export:
                    //Сброс строки поиска, что бы взять данные из scanData
                    searchBox.Clear();

                    scanData.SelectedItems.Clear();

                    OverlayText.Text = CommonConstants.OverlayDxf;
                    txtStatus.Text = "Выгрузка Dxf...";
                    break;
            }
            
            CheckExit.ClearData();
            OverlayGrid.Visibility = Visibility.Visible;
            
            DesaturateOverlay.Height = 100;
        }

        /// <summary>
        /// Пост обработка
        /// </summary>
        private void AfterProcess(CallSource source)
        {
            OverlayGrid.Visibility = Visibility.Collapsed;
            OverlayText.Text = CommonConstants.OverlayScan;

            OverlayProcessText.Visibility = Visibility.Collapsed;
            
            if (String.IsNullOrEmpty(CheckExit.SystemMessage))
            {
                txtStatus.Text = source switch
                {
                    CallSource.Scan => "Готов к выгрузке dxf",
                    CallSource.Export => "Готов к сканированию",
                    _ => txtStatus.Text
                };
            }
        }

        /// <summary>
        /// Изменяет материал в выбранных строках
        /// </summary>
        /// <param name="selected">Выбранные строки</param>
        private async void ChangeMaterial(List<StructureClass> selected)
        {
            try
            {
                var materialList = await GetMaterialNamesAsync();

                if (materialList.Count == 0)
                {
                    MessageBox.Show("Не удалось получить список материалов из Inventor");
                    return;
                }

                // выбор материала пользователем
                var wpfWin = new MaterialSelection(materialList, "Выберите материал для замены:", SelectionMode.Single);
                if (wpfWin.ShowDialog() != true) { return; }

                var choiceResult = wpfWin.SelectedMaterials;
                if (choiceResult == null) { return; }

                //замена производится относительно первого выбранного элемента
                //var first = scanData.SelectedItems[0] as StructureClass;
                
                foreach (StructureClass structureClass in selected)
                {
                    structureClass.Material = choiceResult[0];
                    // Обновляем статус
                    if (!string.IsNullOrWhiteSpace(structureClass.Status))
                    {
                        structureClass.Status += "\n";
                    }
                    structureClass.Status += $"Новый материал: {choiceResult[0]}";
                    structureClass.RowColor = System.Windows.Media.Brushes.Azure;

                }
            }
            catch (Exception e)
            {
                Debug.WriteLine(e.StackTrace);
            }
        }

        /// <summary>
        /// Добавляет материал в выбранных строках
        /// </summary>
        /// <param name="selected">Выбранные строки</param>
        private async void AddMaterial(List<StructureClass> selected)
        {
            try
            {
                var materialList = await GetMaterialNamesAsync();

                if (materialList.Count == 0)
                {
                    MessageBox.Show("Не удалось получить список материалов из Inventor");
                    return;
                }

                // выбор материала пользователем
                var wpfWin = new MaterialSelection(materialList, "Выберите материал для добавления:",
                    SelectionMode.Multiple);
                if (wpfWin.ShowDialog() != true) { return; }

                var choiceResult = wpfWin.SelectedMaterials;
                if (choiceResult == null || choiceResult.Count == 0) return;

                var procData = scanData.ItemsSource as ObservableCollection<StructureClass>;

                // Чтобы не было проблем с изменением коллекции во время перебора — 
                // работаем с копией списка индексов или оригинальных элементов
                var selectedCopy = selected.ToList(); // фиксируем текущее состояние

                foreach (string newMaterial in choiceResult)
                {
                    // Проходим по оригинальным элементам в порядке их появления в коллекции
                    for (int i = 0; i < procData.Count; i++)
                    {
                        var original = procData[i];

                        // Проверяем, что это один из выбранных и материал отличается
                        if (!selectedCopy.Contains(original)) continue;
                        if (original.Material == newMaterial) continue;

                        // Создаём копию
                        var copy = original.Clone();
                        copy.Material = newMaterial;

                        // Обновляем статус
                        if (!string.IsNullOrWhiteSpace(copy.Status))
                        {
                            copy.Status += "\n";
                        }
                        copy.Status += $"Новый материал: {newMaterial}";
                        copy.RowColor = System.Windows.Media.Brushes.Azure;

                        // Вставляем СРАЗУ ПОСЛЕ оригинала
                        procData.Insert(i + 1, copy);

                        // Важно: после вставки все последующие индексы сдвигаются → увеличиваем i
                        i++;
                    }
                }
                /*
                foreach (string se in choiceResult)
                {
                    foreach (var copy in 
                             from structureClass in selected 
                             where structureClass.Material != se 
                             select structureClass.Clone())
                    {
                        copy.Material = se;
                        //если строка состояния не пустая, то добавить перенос
                        if (!string.IsNullOrWhiteSpace(copy.Status))
                        {
                            copy.Status += "\n";
                        }
                        copy.Status += $"Новый материал: {se}";
                        copy.RowColor = Brushes.Azure;
                        procData.Add(copy);
                    }
                }
                */
            }
            catch (Exception e)
            {
                Debug.WriteLine(e.StackTrace);
            }
        }


        /// <summary>
        /// Добавляет материал в выбранных строках
        /// </summary>
        /// <param name="selected">Выбранные строки</param>
        private async void AddMaterial_old(List<StructureClass> selected)
        {
            try
            {
                var materialList = await GetMaterialNamesAsync();

                if (materialList.Count == 0)
                {
                    MessageBox.Show("Не удалось получить список материалов из Inventor");
                    return;
                }

                // выбор материала пользователем
                var wpfWin = new MaterialSelection(materialList, "Выберите материал для добавления:", 
                    SelectionMode.Multiple);
                if (wpfWin.ShowDialog() != true) { return; }

                var choiceResult = wpfWin.SelectedMaterials;
                if (choiceResult == null) { return; }

                //добавление производится относительно первого выбранного элемента
                //var first = scanData.SelectedItems[0] as StructureClass;
                //var resultMaterial = string.Join(";", new[] { first.Material }.Concat(choiceResult));
                var addMaterial = string.Join("; ", choiceResult);

                foreach (StructureClass structureClass in selected)
                {
                    //structureClass.AddMaterial = choiceResult;
                    //structureClass.Material = resultMaterial;

                    //если строка состояния не пустая, то добавить перенос
                    if (!string.IsNullOrWhiteSpace(structureClass.Status))
                    {
                        structureClass.Status += "\n";
                    }
                    structureClass.Status += $"Добавлен материал: {addMaterial}";
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine(e.StackTrace);
            }
        }
        
        /// <summary>
        /// Получение списка материалов из Inventor
        /// </summary>
        /// <returns>Список материалов</returns>
        private async Task<List<string>> GetMaterialNamesAsync()
        {
            if (!_host.IsInitialized)
            {
                return new List<string>();
            }

            try
            {
                return await _host.RunAsync(app =>
                {
                    var lib = app.ActiveMaterialLibrary;
                    if (lib == null)
                    {
                        // Библиотека не активна (нет документов / проект не настроен)
                        return Task.FromResult(new List<string>());
                    }

                    var assets = lib.MaterialAssets;
                    var names = new List<string>(assets.Count);
                    names.AddRange(from Asset asset in assets select asset.DisplayName ?? asset.Name);
                    names.Sort();

                    return Task.FromResult(names);
                });
            }
            catch (Exception ex)
            {
                // Ловим сюда COM-ошибки, например "ActiveMaterialLibrary не инициализирована"
                // MessageBox.Show($"Ошибка получения материалов: {ex.Message}");
                return new List<string>();
            }
        }

        #endregion

        #region Обработка окна из другого потока

        /// <summary>
        /// метод для возвращения строк из логов
        /// </summary>
        /// <param name="count"></param>
        /// <returns></returns>
        public string GetLinesAsText(int count = 10)
        {
            return Dispatcher.Invoke(() =>
            {
                if (string.IsNullOrEmpty(LogTextBox.Text))
                    return "";

                var lines = LogTextBox.Text
                    .Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries)
                    .TakeLast(count);

                return string.Join(Environment.NewLine, lines);
            });
        }

        /// <summary>
        /// Метод для обновления Overlay из другого потока
        /// </summary>
        /// <param name="message"></param>
        public void UpdateOverlay(string message, string part = "")
        {
            Dispatcher.Invoke(() =>
            {
                OverlayText.Text = message;
                OverlayProcessText.Text = part;
            });
        }
        
        /// <summary>
        /// Перегрузка для показа текста с деталями
        /// </summary>
        /// <param name="showText"></param>
        public void UpdateOverlay(bool showText)
        {
            Dispatcher.Invoke(() =>
            {
                OverlayProcessText.Visibility = showText ? Visibility.Visible : Visibility.Collapsed;
            });
        }

        /// <summary>
        /// Метод для обновления LogTextBox из другого потока
        /// </summary>
        /// <param name="message"></param>
        /// <param name="needTime"></param>
        public void UpdateLog(string message, bool needTime = true)
        {
            Dispatcher.Invoke(() =>
            {
                string textTime = needTime 
                    ? DateTime.Now.ToString("HH:mm:ss") + "\t" 
                    : "";
                LogTextBox.AppendText(textTime + message + Environment.NewLine);

                // надёжный способ для прокрутки (да, 2 раза)
                LogTextBox.CaretIndex = LogTextBox.Text.Length;
                LogTextBox.ScrollToEnd();
                LogTextBox.ScrollToEnd();
            });
        }

        /// <summary>
        /// Метод для закрашивания иконки
        /// <param name="progress">Установить значение</param>
        /// </summary>
        public void SetProgress(double progress)
        {
            Dispatcher.Invoke(() =>
            {
                double maxHeight = Logo.ActualHeight;
                DesaturateOverlay.Height = maxHeight * (1 - progress / 100);
            });
        }

        /// <summary>
        /// Метод для закрашивания иконки
        /// </summary>
        /// <param name="procProgress">Процент который нужно вычесть</param>
        public void MinusProgress(double procProgress)
        {
            Dispatcher.Invoke(() =>
            {
                double maxHeight = Logo.ActualHeight; 
                double decreaseBy = maxHeight * procProgress;

                //DesaturateOverlay.Height -= maxHeight*procProgress;
                // Вычитаем и сразу ограничиваем снизу нулём
                DesaturateOverlay.Height = Math.Max(0, DesaturateOverlay.Height - decreaseBy);
            });
        }

        /// <summary>
        /// Меняет цвет для деталей в процессе обработки
        /// </summary>
        public void ChangeColor(StructureClass structureClass)
        {
            Dispatcher.Invoke(() =>
            {
                structureClass.RowColor = System.Windows.Media.Brushes.LightGreen;
                //structureClass.Status = "";
            });
        }

        /// <summary>
        /// Меняет цвет для деталей в процессе обработки
        /// </summary>
        public void ChangeColor(StructureClass structureClass,string infoStatus)
        {
            Dispatcher.Invoke(() =>
            {
                structureClass.RowColor = System.Windows.Media.Brushes.LightCoral;
                if (!string.IsNullOrWhiteSpace(structureClass.Status))
                {
                    structureClass.Status += "\n";
                }
                structureClass.Status += infoStatus;
            });
        }
        
        /// <summary>
        /// Выделяет указанную строку в списке и прокручивает её в видимую область
        /// </summary>
        /// <param name="structureClass">Элемент, который нужно выделить</param>
        public void SelectAndScrollToItem(StructureClass structureClass)
        {
            Dispatcher.Invoke(() =>
            {
                scanData.SelectedItem = structureClass;

                // Даём WPF немного времени на обновление layout (важно при виртуализации)
                Dispatcher.InvokeAsync(() =>
                {
                    scanData.UpdateLayout();
                    if (scanData.SelectedItem != null)
                    {
                        scanData.ScrollIntoView(scanData.SelectedItem);
                    }
                }, DispatcherPriority.Background);

            });
        }

        /// <summary>
        /// Выделяет указанную строку в списке и прокручивает её в видимую область
        /// </summary>
        /// <param name="structureClass">Элемент, который нужно выделить</param>
        public void SelectAndScrollToItem()
        {
            Dispatcher.Invoke(() =>
            {
                scanData.SelectedItem = null;
            });
        }

        #endregion

        #region Обработка окна

        /// <summary>
        /// Выделяет детали с ошибками в окне
        /// </summary>
        /// <param name="scannedData">Данные сканирования</param>
        private static void SelectDetails(ObservableCollection<StructureClass>? scannedData)
        {
            foreach (StructureClass structureClass in scannedData)
            {
                if (structureClass.NoFlat)
                {
                    structureClass.RowColor = System.Windows.Media.Brushes.LightGray; // файл с проблемой
                    structureClass.Status = ErrorsConst.NoFlat;
                }
                else if (structureClass.NullFlat)
                {
                    structureClass.RowColor = System.Windows.Media.Brushes.LightGray; // файл с проблемой
                    structureClass.Status = ErrorsConst.NullFlat;
                }
            }
        }


        /// <summary>
        /// Процесс поиска в таблице
        /// </summary>
        private void SearchProc()
        {
            if (_scanResult?.ScannedData == null) { return; }

            string searchText = searchBox.Text.Trim().ToLower() ?? "";

            if (string.IsNullOrWhiteSpace(searchText))
            {
                scanData.ItemsSource = _scanResult.ScannedData;
                return;
            }

            // Фильтруем в зависимости от выбранного столбца
            List<StructureClass> filtered = _scanResult.ScannedData.ToList();

            switch ((string)searchComboBox.SelectedValue)
            {
                case HeaderConst.Обозначение:
                    filtered = _scanResult.ScannedData
                        .Where(x => !string.IsNullOrEmpty(x.PartNumber) &&
                                    x.PartNumber.ToLower().Contains(searchText)).ToList();
                    break;
                case HeaderConst.Наименование:
                    filtered = _scanResult.ScannedData
                        .Where(x => !string.IsNullOrEmpty(x.Description) &&
                                    x.Description.ToLower().Contains(searchText)).ToList();
                    break;
                case HeaderConst.Материал:
                    filtered = _scanResult.ScannedData
                        .Where(x => !string.IsNullOrEmpty(x.Material) &&
                                    x.Material.ToLower().Contains(searchText)).ToList();
                    break;
                case HeaderConst.Толщина:
                    filtered = _scanResult.ScannedData
                        .Where(x => !string.IsNullOrEmpty(x.Thickness.ToString()) &&
                                    x.Thickness.ToString().ToLower().Contains(searchText)).ToList();
                    break;
                case HeaderConst.Количество:
                    filtered = _scanResult.ScannedData
                        .Where(x => !string.IsNullOrEmpty(x.Quantity.ToString()) &&
                                    x.Quantity.ToString().ToLower().Contains(searchText)).ToList();
                    break;
            }

            scanData.ItemsSource = new ObservableCollection<StructureClass>(filtered);
        }
        
        /// <summary>
        /// Получение версии приложения
        /// </summary>
        /// <returns></returns>
        private string GetAppVersion()
        {
            // Самый надёжный способ для большинства WPF-приложений
            var assembly = Assembly.GetExecutingAssembly();
            var version = assembly.GetName().Version;

            return version?.ToString() ?? "?.?.?.?";
        }

        /// <summary>
        /// Обновляет тул тип в зависимости от выбранной позиции
        /// </summary>
        private void UpdateTooltip()
        {
            if (userDirect != null)
            {
                userDirect.ToolTip = userDirect?.IsChecked == true
                    ? "При создании dxf можно будет выбрать папку для сохранения файлов" //Включено: 
                    : "Используется папка по умолчанию - 'Чертежи' если проект находится в 'Модель'"; //Выключено
            }

            if (categorizeMaterial != null)
            {
                categorizeMaterial.ToolTip = categorizeMaterial?.IsChecked == true
                    ? "Будет создана подпапка с материалом"
                    : "Не будет создана подпапка с материалом";
            }

            if (categorizeThickness != null)
            {
                categorizeThickness.ToolTip = categorizeThickness?.IsChecked == true
                    ? "Будет создана подпапка с толщиной"
                    : "Не будет создана подпапка с толщиной";
            }

        }


        #endregion


    }

}
