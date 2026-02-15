using System.Globalization;
using System.Windows;
using System.Windows.Markup;

namespace DxfExporter
{
    /// <summary>
    /// Interaction logic for App.xaml.
    /// </summary>
    public partial class App : Application
    {
        /// <summary>
        /// Инициализирует культуру привязок WPF в соответствии с текущей культурой ОС,
        /// чтобы десятичные значения в биндингах корректно обрабатывали региональный разделитель.
        /// </summary>
        protected override void OnStartup(StartupEventArgs e)
        {
            var currentCulture = CultureInfo.CurrentCulture;

            CultureInfo.DefaultThreadCurrentCulture = currentCulture;
            CultureInfo.DefaultThreadCurrentUICulture = currentCulture;

            FrameworkElement.LanguageProperty.OverrideMetadata(
                typeof(FrameworkElement),
                new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(currentCulture.IetfLanguageTag)));

            base.OnStartup(e);
        }
    }
}
