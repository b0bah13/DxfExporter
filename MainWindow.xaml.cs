using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using DxfExporter.Constants;
using DxfExporter.Export_Dxf;
using DxfExporter.MaskProcess;
using DxfExporter.Scanning;
using DxfExporter.Versions;
using Inventor;
using Microsoft.VisualBasic;
using Microsoft.Win32;
using static System.Net.Mime.MediaTypeNames;
using Environment = System.Environment;
using File = System.IO.File;
using Path = System.IO.Path;
using Point = System.Windows.Point;
using TextBox = System.Windows.Controls.TextBox;
using System.Text.Json.Serialization;
using System.Reflection.Metadata;
using Document = Inventor.Document;
using System.Collections.Specialized;
using System.IO;


namespace DxfExporter
{
    public partial class MainWindow : Window
    {
        #region Объявление переменных
        
        const string defaultPathTable = @"K:\Документы\Инструкции\Автоматизация процессов\Таблица соответствия.xlsx";
        private static readonly string SettingsFilePath = Path.Combine(Path.GetTempPath(), "DxfExporter", "settings-tab-state.json");

        private CancellationTokenSource _cts;
        private ScanResult _scanResult;
        private ObservableCollection<StructureClass> _displayScanData = new();
        private readonly List<TourStep> _tourSteps = new();
        private int _tourIndex;
        private bool _isTourActive;
        private bool _isApplyingSettings;
        private bool _isSettingsInitialized;
        private bool _isColumnWidthSyncSubscribed;
        private readonly HashSet<DataGrid> _openedDetailsGrids = new();

        private static readonly JsonSerializerOptions SettingsJsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        public string AppVersion { get; private set; }
        public string _scanFilePath,_exportDir;

        public MaskEditorViewModel MaskVm { get; } = new MaskEditorViewModel();
        public StateWithMessage CheckExit = new StateWithMessage { };
        public ExportSettings FolderSettings = new ExportSettings();
        public CheckFileSettings CheckSettings = new CheckFileSettings();

        /// <summary>
        /// Перечисление для указания источника вызова. 
        /// </summary>
        public enum CallSource
        {
            Scan,
            Export,
            AddOpen,
            ChoiceInFolder
        }

        /// <summary>
        /// Класс для хранения данных о настройках структуры папок
        /// </summary>
        public class ExportSettings
        {
            public bool SubFolderMaterials { get; set; } = true;
            public bool SubFolderThickness { get; set; } = true;
            public bool UserDxfDir { get; set; } = false;
        }

        /// <summary>
        /// Класс для хранения данных для проверки габарита
        /// </summary>
        public class CheckFileSettings
        { 
            public bool CheckGab { get; set; } = true;
            public string PathTable { get; set; } = defaultPathTable;
        }

        /// <summary>
        /// Класс для хранения настроек
        /// </summary>
        private sealed class SettingsTabState
        {
            public bool UserDxfDir { get; set; }
            public bool CategorizeMaterial { get; set; } = true;
            public bool CategorizeThickness { get; set; } = true;
            public bool CheckGab { get; set; } = true;
            public string? TablePath { get; set; }
            public string? TemplateMode { get; set; }
            public string? CustomText { get; set; }
            public List<string> MaskParts { get; set; } = new List<string>();
        }
        
        #endregion

        #region Данные для фраз

        /// <summary>
        /// Все загруженные цитаты
        /// </summary>
        private List<Quote> _quotes = new List<Quote>();

        /// <summary>
        /// Перемешанный список индексов
        /// </summary>
        private List<int> _shuffledIndexes = new List<int>();

        /// <summary>
        /// Текущая позиция в списке
        /// </summary>
        private int _currentIndex = 0;

        /// <summary>
        /// Таймер смены цитат
        /// </summary>
        private DispatcherTimer _quoteTimer;

        /// <summary>
        /// Генератор случайных чисел
        /// </summary>
        private readonly Random _random = new Random();

        const string quoteFile = @"K:\Автоматизация процессов\Report\Фразы.json";

        /// <summary>
        /// Простая модель цитаты: только текст
        /// </summary>
        public class Quote
        {
            [JsonPropertyName("text")] public string Text { get; set; } = string.Empty;
        }

        #endregion
        
        public MainWindow()
        {
            InitializeComponent();
            SubscribeMainGridColumnWidthSync();

            //DataContext = this;
            DataContext = MaskVm;
            TablePath.Text = defaultPathTable;
            AppVersion = GetAppVersion();   // или просто присвоить строку

            UpdateTooltip();

            // для обработки закрытия формы в другом потоке
            _cts = new CancellationTokenSource();

            MaskVm.MaskParts.CollectionChanged += MaskParts_CollectionChanged;
            LoadSettingsTabState();
            // Разрешаем сохранение только после завершения первичной инициализации окна.
            _isSettingsInitialized = true;

            // отображение окна с информацией об изменениях
            var (needShow, unseenVersions) = ShowNewVersion.CheckNeedShow();

            if (needShow)
            {
                ShowNewVers(unseenVersions);
            }
            
            Loaded += MainWindow_Loaded;
            SizeChanged += MainWindow_SizeChanged;
            SourceInitialized += MainWindow_SourceInitialized;
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
        
        #region Корректная максимизация окна с WindowStyle=None

        /// <summary>
        /// Добавляет WinAPI-хук после создания HWND, чтобы корректно обрабатывать размеры
        /// окна при разворачивании на любом мониторе.
        /// </summary>
        private void MainWindow_SourceInitialized(object? sender, EventArgs e)
        {
            if (PresentationSource.FromVisual(this) is HwndSource source)
            {
                source.AddHook(WndProc);
            }
        }

        /// <summary>
        /// Обрабатывает оконные сообщения и перехватывает запрос на расчет габаритов
        /// максимизированного окна.
        /// </summary>
        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WmGetMinMaxInfoMessage)
            {
                WmGetMinMaxInfo(hwnd, lParam);
                handled = true;
            }

            return IntPtr.Zero;
        }

        /// <summary>
        /// Ограничивает размеры максимизированного окна рабочей областью монитора
        /// (без перекрытия панели задач).
        /// </summary>
        private static void WmGetMinMaxInfo(IntPtr hwnd, IntPtr lParam)
        {
            var minMaxInfo = Marshal.PtrToStructure<MINMAXINFO>(lParam);

            IntPtr monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
            if (monitor != IntPtr.Zero)
            {
                var monitorInfo = new MONITORINFO();
                GetMonitorInfo(monitor, monitorInfo);

                RECT workArea = monitorInfo.rcWork;
                RECT monitorArea = monitorInfo.rcMonitor;

                minMaxInfo.ptMaxPosition.X = Math.Abs(workArea.Left - monitorArea.Left);
                minMaxInfo.ptMaxPosition.Y = Math.Abs(workArea.Top - monitorArea.Top);
                minMaxInfo.ptMaxSize.X = Math.Abs(workArea.Right - workArea.Left);
                minMaxInfo.ptMaxSize.Y = Math.Abs(workArea.Bottom - workArea.Top);
            }

            Marshal.StructureToPtr(minMaxInfo, lParam, true);
        }

        /// <summary>
        /// Идентификатор сообщения WM_GETMINMAXINFO.
        /// </summary>
        private const int WmGetMinMaxInfoMessage = 0x0024;

        /// <summary>
        /// Флаг получения ближайшего монитора для заданного окна.
        /// </summary>
        private const int MonitorDefaultToNearest = 0x00000002;

        /// <summary>
        /// Получает дескриптор монитора, на котором размещено окно.
        /// </summary>
        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr handle, int flags);

        /// <summary>
        /// Заполняет структуру с информацией о мониторе и рабочей области.
        /// </summary>
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, MONITORINFO lpmi);

        /// <summary>
        /// Структура WinAPI точки (X, Y).
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        /// <summary>
        /// Структура WinAPI с ограничениями размеров/позиции окна.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }

        /// <summary>
        /// Структура WinAPI с границами монитора и рабочей области.
        /// </summary>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private class MONITORINFO
        {
            public int cbSize = Marshal.SizeOf(typeof(MONITORINFO));
            public RECT rcMonitor = new RECT();
            public RECT rcWork = new RECT();
            public int dwFlags;
        }

        /// <summary>
        /// Структура WinAPI прямоугольника.
        /// </summary>
        [StructLayout(LayoutKind.Sequential, Pack = 0)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        #endregion

        #endregion

        #region Обработчик кнопок, нажатий клавиш, полей
        
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
            SaveSettingsTabState();
        }
        
        /// <summary>
        /// Окно с информацией о выпущенных версиях
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void VersInfoClick(object sender, RoutedEventArgs e)
        {
            var wpfWin = new InfoWindow("Информация об обновлениях", Versions.VersionHistory.GetFullHistory());
            wpfWin.ShowDialog();
            
            //MessageBox.Show(Versions.VersionHistory.GetFullHistory(),
            //    "Информация об обновлениях", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>
        /// Обработка строки таблицы соответствия
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void TablePath_TextChanged(object sender, TextChangedEventArgs e)
        {
            // На старте элементы могут вызывать события до полной инициализации окна.
            // В этот момент сохранять нельзя, чтобы не перезаписать файл дефолтными значениями.
            if (_isApplyingSettings || !_isSettingsInitialized)
                return;

            TextBox? tBox = sender as TextBox;

            switch (tBox?.Name)
            {
                case "TablePath":
                    TextBoxProc(tBox, defaultPathTable);
                    SaveSettingsTabState();
                    break;
            }
        }
        
        /// <summary>
        /// Кнопка обзор
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void browseButton_Click(object sender, RoutedEventArgs e)
        {
            Button? button = sender as Button;

            switch (button?.Name)
            {
                case "browseButton":
                    TablePath.Text = SelectExcel(@"K:\Документы\Инструкции\Автоматизация процессов\");
                    break;
            }
        }

        /// <summary>
        /// Обработка чекбокса проверки габарита
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void checkGab_Checked(object sender, RoutedEventArgs e)
        {
            var checkBox = sender as CheckBox;

            switch (checkBox.Name)
            {
                case "checkGab":
                    CheckSettings.CheckGab = checkGab.IsChecked == true;
                    break;
            }

            UpdateTooltip();
            SaveSettingsTabState();
        }

        /// <summary>
        /// Открывает папку
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void openButton_Click(object sender, RoutedEventArgs e)
        {
            if(string.IsNullOrEmpty(_exportDir)) return;
            if(!System.IO.Path.Exists(_exportDir))return;
            Process.Start("explorer.exe", $"\"{_exportDir}\"");
        }

        /// <summary>
        /// Кнопка добавления открытых файлов
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void addOpenFilesButton_Click(object sender, RoutedEventArgs e)
        {
            StartAddFiles(CallSource.AddOpen);
        }

        /// <summary>
        /// Кнопка добавления файлов из папки
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void choiceFilesButton_Click(object sender, RoutedEventArgs e)
        {
            StartAddFiles(CallSource.ChoiceInFolder);
        }

        /// <summary>
        /// Очистка результатов сканирования
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void clearListButton_Click(object sender, RoutedEventArgs e)
        {
            LogTextBox.Clear();
            scanData.ItemsSource = null;
            CheckExit.ClearData();
            txtStatus.Text = "Готов к сканированию";
        }

        /// <summary>
        /// Нажатие на значок загрузки
        /// </summary>
        private void Grid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Загружаем цитаты только один раз
            if (_quotes.Count == 0)
            {
                _quotes = LoadQuotes(quoteFile);
                if (_quotes.Count == 0)
                    return;

                ShuffleQuotes();
            }

            // Показать / скрыть блок
            tb_quote.Visibility = tb_quote.Visibility switch
            {
                Visibility.Collapsed => Visibility.Visible,
                Visibility.Visible => Visibility.Collapsed,
                _ => tb_quote.Visibility
            };

            // Инициализируем таймер только при первом показе
            if (tb_quote.Visibility == Visibility.Visible)
            {
                ShowNextQuote();

                if (_quoteTimer == null)
                {
                    _quoteTimer = new DispatcherTimer();
                    _quoteTimer.Interval = TimeSpan.FromSeconds(5);
                    _quoteTimer.Tick += QuoteTimer_Tick;
                }

                _quoteTimer.Start();
            }
            else
            {
                _quoteTimer?.Stop();
            }
        }
        
        /// <summary>
        /// Обрабатывает изменения коллекции частей маски и сохраняет состояние вкладки настроек.
        /// </summary>
        private void MaskParts_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            SaveSettingsTabState();
        }

        /// <summary>
        /// Обрабатывает изменение пользовательского текста маски и сохраняет состояние вкладки настроек.
        /// </summary>
        private void TxtCustomText_TextChanged(object sender, TextChangedEventArgs e)
        {
            SaveSettingsTabState();
        }

        /// <summary>
        /// Восстановление базовых настроек
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void clearSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            userDirect.IsChecked = false;
            categorizeMaterial.IsChecked = true;
            categorizeThickness.IsChecked = true;
            checkGab.IsChecked = true;
            TablePath.Text = defaultPathTable;
            templateComboBox.SelectedIndex = 0;
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
            SaveSettingsTabState();

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

            SaveSettingsTabState();
        }

        /// <summary>
        /// Добавляет выбранный заголовок в маску по двойному клику в левом списке.
        /// </summary>
        private void AvailableList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (AvailableList.SelectedItem is HeaderItem item)
            {
                MaskVm.AddHeaderToMask(item);
            }

            SaveSettingsTabState();
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
            SaveSettingsTabState();
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

            SaveSettingsTabState();
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
            SaveSettingsTabState();
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
            SaveSettingsTabState();
        }

        /// <summary>
        /// Удаляет выбранный элемент из правого списка (SelectedList)
        /// </summary>
        private void RemoveSelected_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedList.SelectedItem is MaskPart part)
                MaskVm.RemovePart(part);

            SaveSettingsTabState();
        }
        
        /// <summary>
        /// Полностью очищает правый список (SelectedList)
        /// </summary>
        private void ClearMaskParts_Click(object sender, RoutedEventArgs e)
        {
            MaskVm.ClearMaskParts();
            SaveSettingsTabState();
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

        /// <summary>
        /// Обрабатывает drop в левый список: удаляет элемент из маски и сохраняет состояние.
        /// </summary>
        private void AvailableList_Drop(object sender, DragEventArgs e)
        {
            // Перетаскивание обратно из правого → снимаем флаг IsUsed
            if (e.Data.GetDataPresent(typeof(MaskPart)))
            {
                if (e.Data.GetData(typeof(MaskPart)) is MaskPart droppedPart)
                    MaskVm.DropToAvailableList(droppedPart);

                SaveSettingsTabState();
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
            SaveSettingsTabState();
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
            var selected = GetSelectedRowsForActions(sender);
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
            var selected = GetSelectedRowsForActions(sender);
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

            var selectedRows = GetSelectedRowsForActions(sender);
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

            var selectedRows = GetSelectedRowsForActions(sender);
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
            if (sender is not DataGrid currentGrid)
            {
                e.Handled = true;
                return;
            }

            if (currentGrid.SelectedItems.Count == 0)
            {
                e.Handled = true;           // ← это ключевое — отменяет открытие меню
                return;
            }

            // Дополнительная проверка — меню только при клике по строке
            var pos = Mouse.GetPosition(currentGrid);
            var hit = VisualTreeHelper.HitTest(currentGrid, pos);

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
        /// Для обычного клика по строке очищает выделение в остальных таблицах.
        /// Это синхронизирует поведение основной и вложенных таблиц как у единого выбора.
        /// </summary>
        private void DataGridRow_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                return;
            }

            if (sender is not DataGridRow row)
            {
                return;
            }

            var ownerGrid = ItemsControl.ItemsControlFromItemContainer(row) as DataGrid;
            if (ownerGrid == null)
            {
                return;
            }

            ClearSelectionInOtherGrids(ownerGrid);
        }

        private void ClearSelectionInOtherGrids(DataGrid activeGrid)
        {
            if (!ReferenceEquals(scanData, activeGrid) && scanData.SelectedItems.Count > 0)
            {
                scanData.SelectedItems.Clear();
            }

            foreach (var nestedGrid in _openedDetailsGrids.ToList())
            {
                if (nestedGrid == null || !nestedGrid.IsLoaded)
                {
                    _openedDetailsGrids.Remove(nestedGrid);
                    continue;
                }

                if (ReferenceEquals(nestedGrid, activeGrid))
                {
                    continue;
                }

                if (nestedGrid.SelectedItems.Count > 0)
                {
                    nestedGrid.SelectedItems.Clear();
                }
            }
        }

        /// <summary>
        /// Срабатывает каждый раз при открытии контекстного меню
        /// Здесь мы синхронизируем состояние чекбоксов с первой выбранной строкой
        /// </summary>
        private void ContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            if (sender is not ContextMenu menu) return;
            if (menu.PlacementTarget is not DataGrid currentGrid) return;
            if (currentGrid.SelectedItems.Count == 0) return;

            var first = currentGrid.SelectedItems[0] as StructureClass;
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

        /// <summary>
        /// Инвертирует отметки выгрузки
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MenuItem_InvertCheck_Click(object sender, RoutedEventArgs e)
        {
            var selected = GetSelectedRowsForActions(sender);
            if (selected.Count == 0) return;

            var all = FlattenForProcessing(_displayScanData).ToList();

            // Сбрасываем у всех
            foreach (var row in all)
            {
                row.NeedUnload = false;
            }

            // Включаем только у выделенных
            foreach (var row in selected)
            {
                row.NeedUnload = true;
            }

            // Один проход по всем строкам для обновления
            foreach (var row in all)
            {
                row.UpdateUnloadProp();
            }
        }

        /// <summary>
        /// Открывает выбранные детали
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MenuItem_OpenFile_Click(object sender, RoutedEventArgs e)
        {
            var paths = GetSelectedRowsForActions(sender)
                .Select(x => x.Path)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct()
                .ToList();

            if (paths.Count == 0) return;

            OpenDocumentsForUser(paths);
        }


        /// <summary>
        /// Возвращает выбранные строки для действий контекстного меню.
        /// Если выбрана строка-группа expander, в выборку включаются её дочерние исполнения.
        /// </summary>
        private List<StructureClass> GetSelectedRowsForActions(object sender)
        {
            var grid = ResolveContextDataGrid(sender);
            if (grid == null) return new List<StructureClass>();

            return grid.SelectedItems
                .OfType<StructureClass>()
                .SelectMany(row => row.IsExpanderGroup ? row.ChildMembers : (IEnumerable<StructureClass>)new List<StructureClass> { row })
                .Distinct()
                .ToList();
        }

        private static DataGrid? ResolveContextDataGrid(object sender)
        {
            if (sender is ContextMenu contextMenu && contextMenu.PlacementTarget is DataGrid targetGrid)
            {
                return targetGrid;
            }

            if (sender is DependencyObject dependencyObject)
            {
                var contextMenuFromParent = FindParentContextMenu(dependencyObject);
                if (contextMenuFromParent?.PlacementTarget is DataGrid menuGrid)
                {
                    return menuGrid;
                }
            }

            return null;
        }

        private static ContextMenu? FindParentContextMenu(DependencyObject? start)
        {
            DependencyObject? current = start;
            while (current != null)
            {
                if (current is ContextMenu contextMenu)
                {
                    return contextMenu;
                }

                current = current switch
                {
                    FrameworkElement frameworkElement => frameworkElement.Parent,
                    FrameworkContentElement frameworkContentElement => frameworkContentElement.Parent,
                    _ => null
                };
            }

            return null;
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
                Target = addOpenFilesButton,
                Title = "Добавление файлов",
                Description = "Добавляет в таблицу открытые листовые детали."
            });
            _tourSteps.Add(new TourStep
            {
                Tab = HomeTab,
                Target = choiceFilesButton,
                Title = "Добавление файлов",
                Description = "Добавляет в таблицу листовые детали выбранные пользователем из папки."
            });
            _tourSteps.Add(new TourStep
            {
                Tab = HomeTab,
                Target = clearListButton,
                Title = "Очистка",
                Description = "Очищает таблицу."
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
                Target = openButton,
                Title = "Открыть папку",
                Description = "Открывает папку с выгруженными развёртками."
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
                Description = "В каждой строке можно изменять ячейки:\n" +
                              $"\"{HeaderConst.Обозначение}\", \"{HeaderConst.Наименование}\", \"{HeaderConst.Толщина}\", \"{HeaderConst.Количество}\"\n" +
                              "Для изменения необходимо 2 раза нажать на неё.\n" +
                              "\nПри нажатии ПКМ открывается контекстное меню, где можно выбрать свойства выгрузки.\n" +
                              "Описание значений в столбце \"Свойства выгрузки\":\n" +
                              "Грав. - выгружается гравировка\n" +
                              "Гиб - выгружаются линии гиба\n" +
                              "Шаблон - DXF файлы будут выгружены в папку \"Шаблоны\"\n" +
                              "\nОпция выбора 'Оставить выбранные' оставляет свойство 'Выгрузить' только на выбранных строках.\n" +
                              "'Открыть деталь' - открывает все выбранные детали, если они не открыты."
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
                Target = gbCheckGab,
                Title = "Проверка развёртки",
                Description = "Включает проверку развёртки при выгрузке.\nДанные по листам берутся из 'Таблицы соответствия'."
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
            _tourSteps.Add(new TourStep
            {
                Tab = SettingsTab,
                Target = clearSettingsButton,
                Title = "Восстановление настроек",
                Description = "Восстанавливает базовые настройки программы."
            });

            
            // _tourSteps.Add(new TourStep { Tab = HomeTab, Target = <имя_элемента>, Title = "<заголовок>", Description = "<описание>" });
            // _tourSteps.Add(new TourStep { Tab = SettingsTab, Target = <имя_элемента>, Title = "<заголовок>", Description = "<описание>" });
        }

        /// <summary>
        /// Запускает ознакомительный режим с первого шага.
        /// </summary>
        private void StartTour(int tourStep = 0)
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
            _tourIndex = tourStep;
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
            TourTooltipBorder.Measure(new System.Windows.Size(300, double.PositiveInfinity));
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

                ScanningProcess _processor = new ScanningProcess(this, CheckSettings);
                _scanResult = await _processor.StartProcessingAsync(_cts.Token, CallSource.Scan); // Передаем токен отмены

                if (CheckExit.NeedExit)
                {
                    txtStatus.Text = CheckExit.Message;
                    return;
                }
                // Перевод результатов сканирования в список для вывода
                if (_scanResult == null) { return; }

                txtStatus.Text = $"Сканирование завершено";
                
                _displayScanData = BuildDisplayScanData(_scanResult.ScannedData);
                scanData.ItemsSource = _displayScanData;

                foreach (StructureClass structureClass in FlattenForProcessing(_displayScanData))
                {
                    structureClass.UpdateUnloadProp();
                }

                // подсветить детали с ошибками
                SelectDetails(FlattenForProcessing(_displayScanData));

            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.StackTrace);
                txtStatus.Text = IsInventorConnectionError(ex)
                    ? "Не удалось подключиться к Inventor"
                    : "Ошибка при сканировании";

                if (IsInventorConnectionError(ex))
                {
                    MessageBox.Show(ex.Message, "Inventor", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
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

                int unloadCount = FlattenForProcessing(_displayScanData).Count(structureClass => structureClass.NeedUnload);
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

                var procData = FlattenForProcessing(_displayScanData);
                var maskData = MaskVm.MaskParts;
                var modeName = MaskVm.ModeName;


                ExportProcess _export = new ExportProcess(this);
                _exportDir = await _export.StartProcessExport(_cts.Token, procData, maskData, modeName,
                    _scanFilePath, FolderSettings, unloadCount);

                if (CheckExit.NeedExit)
                {
                    txtStatus.Text = CheckExit.Message;
                    return;
                }

                txtStatus.Text = "Выгрузка Dxf завершена";
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.StackTrace);
                txtStatus.Text = IsInventorConnectionError(ex)
                    ? "Не удалось подключиться к Inventor"
                    : "Ошибка при выгрузке dxf";

                if (IsInventorConnectionError(ex))
                {
                    MessageBox.Show(ex.Message, "Inventor", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
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
                    _displayScanData.Clear();

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
            tb_quote.Visibility = Visibility.Collapsed;

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
            tb_quote.Visibility = Visibility.Collapsed;

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

                var procData = FlattenForProcessing(_displayScanData);

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
        /// Получение списка материалов из Inventor
        /// </summary>
        /// <returns>Список материалов</returns>
        private async Task<List<string>> GetMaterialNamesAsync()
        {
            try
            {
                return await InventorHost.Instance.Value.RunAsync(app =>
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
        
        /// <summary>
        /// Обработка логики в текстовом поле
        /// </summary>
        /// <param name="tBox"></param>
        /// <param name="defpath"></param>
        /// <param name="path"></param>
        private void TextBoxProc(TextBox tBox, string defpath)
        {
            if (string.IsNullOrEmpty(tBox.Text))
            {
                tBox.Text = defpath;
                CheckSettings.PathTable = defpath;
                return;
            }

            if (!System.IO.File.Exists(tBox.Text))
            {
                MessageBox.Show("Указанного файла не существует!\nБудет выбран стандартный файл таблицы соответствия.",
                    "Ошибка выбора файла", MessageBoxButton.OK, MessageBoxImage.Error);
                tBox.Text = defpath;
                CheckSettings.PathTable = defpath;
            }
            else
            {
                CheckSettings.PathTable = tBox.Text;
            }
        }

        /// <summary>
        /// Выбор файла Excel
        /// </summary>
        /// <param name="initialDirectory">Первичная папка поиска</param>
        /// <returns></returns>
        private string SelectExcel(string initialDirectory)
        {
            string originalText = TablePath.Text;

            // Создаем экземпляр OpenFileDialog
            OpenFileDialog openFileDialog = new OpenFileDialog();

            // Устанавливаем начальный каталог
            openFileDialog.InitialDirectory = initialDirectory;

            // Устанавливаем фильтр для отображения только Excel-файлов
            openFileDialog.Filter = "Все файлы Excel (*.xlsx;*.xlsm;*.xls)|*.xlsx;*.xlsm;*.xls";

            openFileDialog.Multiselect = false;

            // Устанавливаем заголовок окна
            openFileDialog.Title = "Выберите Excel файл";

            // Показываем диалоговое окно и проверяем результат
            string filePath = openFileDialog.ShowDialog() == true
                ?
                // Получаем выбранный путь к файлу
                openFileDialog.FileName
                : originalText;

            return filePath;
        }

        /// <summary>
        /// Процесс для добавления открытых файлов.
        /// </summary>
        private async void StartAddFiles(CallSource callSource)
        {
            string errText = "Ошибка при добавлении файлов";
            try
            {
                // Дать UI возможность обновить интерфейс
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);

                List<string> choiceFiles = null;
                //если добавляем из папки
                if (callSource == CallSource.ChoiceInFolder)
                {
                    errText = "Ошибка при выборе файлов";

                    // Получаем путь родительской папки того файла который сканируем
                    string parentDirectory = string.IsNullOrWhiteSpace(_scanFilePath)
                        ? string.Empty
                        : Path.GetDirectoryName(_scanFilePath) ?? string.Empty;

                    // Выбираем детали inventor
                    choiceFiles = SelectedFiles(parentDirectory);

                    if (choiceFiles?.Count == 0) return;
                }

                ScanningProcess _processor = new ScanningProcess(this, CheckSettings);
                ScanResult addResult = await _processor.StartProcessingAsync(_cts.Token, 
                    callSource, choiceFiles); 

                if (CheckExit.NeedExit)
                {
                    txtStatus.Text = CheckExit.Message;
                    return;
                }

                _scanResult ??= new ScanResult();

                // Не добавляем уже существующие в таблице детали (с учётом имени состояния/исполнения).
                foreach (var result in addResult.ScannedData)
                {
                    bool exists = _scanResult.ScannedData.Any(x => x.Path == result.Path && x.MemberName == result.MemberName);
                    if (!exists)
                    {
                        _scanResult.ScannedData.Add(result);
                    }
                }

                _displayScanData = BuildDisplayScanData(_scanResult.ScannedData);
                scanData.ItemsSource = _displayScanData;

                foreach (StructureClass structureClass in FlattenForProcessing(_displayScanData))
                {
                    structureClass.UpdateUnloadProp();
                }

                // подсветить детали с ошибками
                SelectDetails(FlattenForProcessing(_displayScanData));

                txtStatus.Text = "Готов к выгрузке dxf";
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.StackTrace);
                txtStatus.Text = IsInventorConnectionError(ex)
                    ? "Не удалось подключиться к Inventor"
                    : errText;

                if (IsInventorConnectionError(ex))
                {
                    MessageBox.Show(ex.Message, "Inventor", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            finally
            {
                
            }
        }
        
        /// <summary>
        /// Выбор файлов ipt
        /// </summary>
        /// <param name="initialDirectory">Папка открываемая по умолчанию</param>
        /// <returns>Пути до выбранных ipt файлов</returns>
        private List<string> SelectedFiles(string initialDirectory)
        {
            // Создаем экземпляр OpenFileDialog
            OpenFileDialog openFileDialog = new OpenFileDialog();
            
            // Устанавливаем начальный каталог
            openFileDialog.InitialDirectory = initialDirectory;

            // Устанавливаем фильтр для отображения только ipt-файлов
            openFileDialog.Filter = "Детали Inventor (*.ipt;)|*.ipt";

            openFileDialog.Multiselect = true;

            // Устанавливаем заголовок окна
            openFileDialog.Title = "Выберите детали Inventor";

            // Показываем диалоговое окно и проверяем результат
            List<string> filesPath = openFileDialog.ShowDialog() == true
                ? openFileDialog.FileNames.ToList()
                : null;

            return filesPath;
        }

        /// <summary>
        /// Открывает несколько документов Inventor для пользователя
        /// </summary>
        public void OpenDocumentsForUser(IEnumerable<string> paths)
        {
            if (paths == null) return;

            // Убираем дубликаты и несуществующие файлы
            var validPaths = paths
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(System.IO.File.Exists)
                .ToList();

            if (validPaths.Count == 0) return;

            try
            {
                InventorHost.Instance.Value.Run(invApp =>
                {
                    Document lastDoc = null;

                    foreach (string path in validPaths)
                    {
                        try
                        {
                            // Если уже открыт — просто активируем
                            Document opened = invApp.Documents.ItemByName[path];
                            opened.Activate();
                            lastDoc = opened;
                        }
                        catch
                        {
                            // Не открыт — открываем
                            Document doc = invApp.Documents.Open(path, true);
                            lastDoc = doc;
                        }
                    }

                    // Активируем последний документ
                    lastDoc?.Activate();
                    invApp.Visible = true;
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Inventor", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// Проверяет удалось ли подключиться к Inventor.
        /// </summary>
        /// <param name="ex"></param>
        /// <returns></returns>
        private static bool IsInventorConnectionError(Exception ex)
        {
            return ex is InvalidOperationException &&
                   ex.Message.Contains("Не удалось подключиться к Inventor", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Загрузка цитат из файла
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
        public static List<Quote> LoadQuotes(string filePath)
        {
            if (!File.Exists(filePath)) return new List<Quote>();

            try
            {
                string json = File.ReadAllText(filePath);
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };
                return JsonSerializer.Deserialize<List<Quote>>(json, options)
                       ?? new List<Quote>();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка чтения файла: {ex.Message}");
                return new List<Quote>();
            }
        }

        /// <summary>
        /// Смена цитаты по таймеру
        /// </summary>
        private void QuoteTimer_Tick(object sender, EventArgs e)
        {
            ShowNextQuote();
        }

        /// <summary>
        /// Перемешивает индексы цитат
        /// </summary>
        private void ShuffleQuotes()
        {
            _shuffledIndexes = Enumerable
                .Range(0, _quotes.Count)
                .OrderBy(x => _random.Next())
                .ToList();

            _currentIndex = 0;
        }

        /// <summary>
        /// Отображает следующую цитату
        /// </summary>
        private void ShowNextQuote()
        {
            if (_quotes.Count == 0)
                return;

            // если дошли до конца — начинаем заново
            if (_currentIndex >= _shuffledIndexes.Count)
                ShuffleQuotes();

            int quoteIndex = _shuffledIndexes[_currentIndex];
            tb_quote.Text = _quotes[quoteIndex].Text;

            _currentIndex++;
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


        /// <summary>
        /// Подписывает базовую таблицу на изменение ширины колонок.
        /// Это нужно, чтобы вложенные таблицы в раскрытии повторяли текущую ширину столбцов.
        /// </summary>
        private void SubscribeMainGridColumnWidthSync()
        {
            if (_isColumnWidthSyncSubscribed || scanData == null) return;

            foreach (var column in scanData.Columns)
            {
                DependencyPropertyDescriptor
                    .FromProperty(DataGridColumn.WidthProperty, typeof(DataGridColumn))
                    ?.AddValueChanged(column, OnMainGridColumnWidthChanged);
            }

            _isColumnWidthSyncSubscribed = true;
        }

        /// <summary>
        /// Обновляет ширины колонок во всех открытых вложенных таблицах.
        /// </summary>
        private void OnMainGridColumnWidthChanged(object? sender, EventArgs e)
        {
            SyncAllOpenedRowDetailsColumns();
        }

        /// <summary>
        /// Событие загрузки деталей строки (RowDetails).
        /// Регистрирует вложенную таблицу и сразу синхронизирует её ширину.
        /// </summary>
        private void scanData_LoadingRowDetails(object sender, DataGridRowDetailsEventArgs e)
        {
            if (e.DetailsElement is not FrameworkElement detailsRoot) return;

            var nestedGrid = FindVisualChild<DataGrid>(detailsRoot);
            if (nestedGrid == null) return;

            _openedDetailsGrids.Add(nestedGrid);
            ApplyBaseColumnsWidthToNested(nestedGrid);
        }

        /// <summary>
        /// Событие выгрузки деталей строки (RowDetails).
        /// Удаляет вложенную таблицу из списка синхронизации.
        /// </summary>
        private void scanData_UnloadingRowDetails(object sender, DataGridRowDetailsEventArgs e)
        {
            if (e.DetailsElement is not FrameworkElement detailsRoot) return;

            var nestedGrid = FindVisualChild<DataGrid>(detailsRoot);
            if (nestedGrid == null) return;

            _openedDetailsGrids.Remove(nestedGrid);
        }

        /// <summary>
        /// Синхронизирует ширины колонок во всех открытых вложенных таблицах.
        /// </summary>
        private void SyncAllOpenedRowDetailsColumns()
        {
            if (_openedDetailsGrids.Count == 0) return;

            foreach (var nestedGrid in _openedDetailsGrids.ToList())
            {
                if (!nestedGrid.IsLoaded)
                {
                    _openedDetailsGrids.Remove(nestedGrid);
                    continue;
                }

                ApplyBaseColumnsWidthToNested(nestedGrid);
            }
        }

        /// <summary>
        /// Копирует текущую ширину колонок основной таблицы во вложенную таблицу.
        /// TODO: при необходимости здесь можно привязать не все колонки, а только часть.
        /// </summary>
        private void ApplyBaseColumnsWidthToNested(DataGrid nestedGrid)
        {
            if (scanData?.Columns == null || nestedGrid?.Columns == null) return;

            int count = Math.Min(scanData.Columns.Count, nestedGrid.Columns.Count);
            for (int i = 0; i < count; i++)
            {
                double width = scanData.Columns[i].ActualWidth;
                if (width > 0)
                {
                    nestedGrid.Columns[i].Width = new DataGridLength(width);
                }
            }
        }

        /// <summary>
        /// Поиск дочернего визуального элемента заданного типа.
        /// </summary>
        private static TChild? FindVisualChild<TChild>(DependencyObject parent) where TChild : DependencyObject
        {
            int childrenCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childrenCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is TChild typedChild)
                {
                    return typedChild;
                }

                var nested = FindVisualChild<TChild>(child);
                if (nested != null)
                {
                    return nested;
                }
            }

            return null;
        }

        #region Обработка окна

        /// <summary>
        /// Выделяет детали с ошибками в окне
        /// </summary>
        /// <param name="scannedData">Данные сканирования</param>
        private static void SelectDetails(IEnumerable<StructureClass>? scannedData)
        {
            if (scannedData == null) return;

            foreach (StructureClass structureClass in scannedData)
            {
                if (structureClass.IsExpanderGroup && structureClass.ChildMembers?.Count > 0)
                {
                    SelectDetails(structureClass.ChildMembers);
                    continue;
                }

                //если ячейка статус не пустая, то добавить перенос строки
                if (!string.IsNullOrWhiteSpace(structureClass.Status))
                {
                    structureClass.Status += "\n";
                }

                if (string.IsNullOrEmpty(structureClass.Path))
                {
                    structureClass.RowColor = System.Windows.Media.Brushes.OrangeRed;
                    continue;
                }

                if (structureClass.NoFlat)
                {
                    structureClass.RowColor = System.Windows.Media.Brushes.LightGray;
                    structureClass.Status += ErrorsConst.NoFlat;
                }
                else if (structureClass.NullFlat)
                {
                    structureClass.RowColor = System.Windows.Media.Brushes.LightGray;
                    structureClass.Status += ErrorsConst.NullFlat;
                }
                else if (structureClass.ErrorMatThick)
                {
                    structureClass.RowColor = System.Windows.Media.Brushes.LightGray;
                    structureClass.Status += ErrorsConst.ErrorMatThick;
                }
                else if (structureClass.FakeThickness)
                {
                    structureClass.RowColor = System.Windows.Media.Brushes.LightGray;
                    structureClass.Status += ErrorsConst.FakeThickness;
                }
                else if (structureClass.BigFlat)
                {
                    structureClass.RowColor = System.Windows.Media.Brushes.LightGray;
                    structureClass.Status += ErrorsConst.BigFlat;
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
                _displayScanData = BuildDisplayScanData(_scanResult.ScannedData);
                scanData.ItemsSource = _displayScanData;
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
                        .Where(x => x.Thickness.HasValue &&
                                    x.Thickness.Value.ToString().ToLower().Contains(searchText)).ToList();
                    break;
                case HeaderConst.Количество:
                    filtered = _scanResult.ScannedData
                        .Where(x => x.Quantity.HasValue &&
                                    x.Quantity.Value.ToString().ToLower().Contains(searchText)).ToList();
                    break;
            }

            _displayScanData = BuildDisplayScanData(new ObservableCollection<StructureClass>(filtered));
            scanData.ItemsSource = _displayScanData;
        }
        


        /// <summary>
        /// Формирует коллекцию для отображения в DataGrid.
        /// Для параметрических и state-деталей создаёт строку-группу с вложенными исполнениями.
        /// </summary>
        private ObservableCollection<StructureClass> BuildDisplayScanData(ObservableCollection<StructureClass> source)
        {
            var display = new ObservableCollection<StructureClass>();

            foreach (var detail in source)
            {
                bool hasChildMembers = detail.ChildMembers != null && detail.ChildMembers.Count > 0;

                if (!hasChildMembers)
                {
                    display.Add(detail);
                    continue;
                }

                var groupHeader = detail.Clone();
                groupHeader.IsExpanderGroup = true;
                groupHeader.IsExpanded = true;
                //groupHeader.GroupMembers = new ObservableCollection<StructureClass>(detail.ChildMembers);
                groupHeader.DisplayName = detail.DisplayName;

                // У строки-заголовка оставляем только имя файла, остальные колонки должны быть пустыми.
                groupHeader.PartNumber = groupHeader.DisplayName;
                groupHeader.Description = string.Empty;
                groupHeader.Material = string.Empty;
                groupHeader.Thickness = null;
                groupHeader.Quantity = null;
                groupHeader.UnloadProp = string.Empty;
                groupHeader.Status = string.Empty;
                groupHeader.Path = string.Empty;

                display.Add(groupHeader);
            }

            return display;
        }

        /// <summary>
        /// Возвращает плоский список строк для внутренней логики (экспорт, поиск, статусы).
        /// </summary>
        private ObservableCollection<StructureClass> FlattenForProcessing(IEnumerable<StructureClass>? source)
        {
            var result = new ObservableCollection<StructureClass>();
            if (source == null) return result;

            foreach (var row in source)
            {
                if (row.IsExpanderGroup)
                {
                    foreach (var member in row.ChildMembers) //.GroupMembers)
                    {
                        result.Add(member);
                    }
                }
                else
                {
                    result.Add(row);
                }
            }

            return result;
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

            if (checkGab != null)
            {
                checkGab.ToolTip = checkGab?.IsChecked == true
                    ? "При создании dxf будет проверен габарит развёртки" //Включено: 
                    : "Габарит развёртки проверятся не будет"; //Выключено
            }

        }
        
        /// <summary>
        /// Загружает состояние элементов вкладки настроек из внешнего JSON-файла, если он существует.
        /// </summary>
        private void LoadSettingsTabState()
        {
            if (!File.Exists(SettingsFilePath))
                return;

            try
            {
                var json = File.ReadAllText(SettingsFilePath);
                var state = JsonSerializer.Deserialize<SettingsTabState>(json);
                if (state == null)
                    return;

                _isApplyingSettings = true;

                userDirect.IsChecked = state.UserDxfDir;
                categorizeMaterial.IsChecked = state.CategorizeMaterial;
                categorizeThickness.IsChecked = state.CategorizeThickness;
                checkGab.IsChecked = state.CheckGab;

                string tablePath = string.IsNullOrWhiteSpace(state.TablePath) ? defaultPathTable : state.TablePath;
                TablePath.Text = File.Exists(tablePath) ? tablePath : defaultPathTable;
                CheckSettings.PathTable = TablePath.Text;

                if (!string.IsNullOrWhiteSpace(state.TemplateMode))
                {
                    templateComboBox.SelectedItem = state.TemplateMode;
                    if (templateComboBox.SelectedItem == null)
                        templateComboBox.SelectedIndex = 0;
                }

                if (templateComboBox.SelectedItem is string template)
                    MaskVm.ApplyTemplate(template);

                if (state.MaskParts.Count > 0)
                {
                    MaskVm.ClearMaskParts();
                    foreach (var part in state.MaskParts
                                 .Where(part => !string.IsNullOrWhiteSpace(part)))
                    {
                        MaskVm.MaskParts.Add(new MaskPart(part));
                    }
                }

                MaskVm.CustomText = state.CustomText ?? string.Empty;
                gridSettings.IsEnabled = MaskVm.IsMaskSettingsEnabled;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка загрузки настроек вкладки: {ex.Message}");
            }
            finally
            {
                _isApplyingSettings = false;
            }
        }

        /// <summary>
        /// Сохраняет текущее состояние элементов вкладки настроек в JSON-файл в temp-папке пользователя.
        /// </summary>
        private void SaveSettingsTabState()
        {
            // На старте элементы могут вызывать события до полной инициализации окна.
            // В этот момент сохранять нельзя, чтобы не перезаписать файл дефолтными значениями.
            if (_isApplyingSettings || !_isSettingsInitialized)
                return;

            try
            {
                var state = new SettingsTabState
                {
                    UserDxfDir = userDirect?.IsChecked == true,
                    CategorizeMaterial = categorizeMaterial?.IsChecked == true,
                    CategorizeThickness = categorizeThickness?.IsChecked == true,
                    CheckGab = checkGab?.IsChecked == true,
                    TablePath = TablePath?.Text ?? string.Empty,
                    TemplateMode = templateComboBox?.SelectedItem?.ToString(),
                    CustomText = txtCustomText?.Text ?? string.Empty,
                    MaskParts = MaskVm.MaskParts
                        .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Text))
                        .Select(x => x.Text)
                        .ToList()
                };

                var settingsDir = Path.GetDirectoryName(SettingsFilePath);
                if (!string.IsNullOrWhiteSpace(settingsDir) && !Directory.Exists(settingsDir))
                    Directory.CreateDirectory(settingsDir);

                File.WriteAllText(SettingsFilePath, JsonSerializer.Serialize(state, SettingsJsonOptions));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка сохранения настроек вкладки: {ex.Message}");
            }
        }

        /// <summary>
        /// Метод для обработки логики отображения окна с информацией о версии
        /// </summary>
        /// <param name="unseenVersions"></param>
        private void ShowNewVers(List<string> unseenVersions)
        {
            foreach (string version in unseenVersions)
            {
                var lastVer = VersionHistory.GetByVersion(version);
                string txtInfo = lastVer.Description;

                ShowNewVersion.ShowInfo(version, txtInfo);
            }

            if (unseenVersions.Count > 1)
            {
                StartTour();
            }
            else
            {
                switch (unseenVersions[0])
                {
                    case "1.0.1.1":
                        StartTour(1);
                        break;
                    case "1.0.1.2":
                        StartTour(16);
                        break;
                }
            }
        }

        #endregion
        
    }

}
