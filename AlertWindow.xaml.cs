using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace DxfExporter
{
    /// <summary>
    /// Логика взаимодействия для AlertWindow.xaml
    /// </summary>
    public partial class AlertWindow : Window
    {
        private readonly DispatcherTimer _autoCloseTimer;

        /// <summary>
        /// Окно-уведомление, которое автоматически закроется через заданное время
        /// </summary>
        /// <param name="imgPath">Полный путь к файлу изображения</param>
        /// <param name="width">Ширина окна</param>
        /// <param name="height">Высота окна</param>
        /// <param name="autoCloseSeconds">Через сколько секунд закрыть (по умолчанию 3)</param>
        public AlertWindow(string imgPath, double width, double height, 
            double left, double top, WindowState windowState,
            double autoCloseSeconds = 1.7)
        {
            InitializeComponent();

            if (windowState == WindowState.Maximized)
            {
                WindowState = WindowState.Maximized;
            }
            else
            {
                Width = width;
                Height = height;
                Left = left;
                Top = top;
            }

            //var bitmap = new BitmapImage();
            //bitmap.BeginInit();
            //bitmap.CacheOption = BitmapCacheOption.OnLoad;     // загружаем сразу полностью
            //bitmap.UriSource = new Uri(imgPath, UriKind.Absolute); // или UriKind.Relative
            //bitmap.EndInit();

            var bitmap = new BitmapImage(new Uri(imgPath));
            alertImage.Source = bitmap;

            // Настраиваем таймер автозакрытия
            _autoCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(autoCloseSeconds) };
            _autoCloseTimer.Tick += (s, e) =>
            {
                _autoCloseTimer.Stop();
                Close();
            };

            Loaded += (s, e) => _autoCloseTimer.Start();
        }

    }
}
