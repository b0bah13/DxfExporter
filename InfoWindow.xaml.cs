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
using System.Globalization;


namespace DxfExporter
{
    /// <summary>
    /// Логика взаимодействия для InfoWindow.xaml
    /// </summary>
    public partial class InfoWindow : Window
    {
        // Генератор случайных чисел
        private readonly Random _random = new Random();

        // Последний выбранный текст (для исключения повторов)
        private string _lastText = null;


        public InfoWindow(string headText, string text)
        {
            InitializeComponent();

            Title = headText;
            infoText.Text = text;

            // Текст кнопки → вес (чем больше, тем чаще выпадает)
            Dictionary<string,int> content = new Dictionary<string, int>()
            {
                {"Восхитительно!", 5},
                {"Умеете, могёте!", 1},
                {"Великолепно!", 6},
                {"Бесподобно!", 7},
                {"Чётко, збс!", 1},
                {"Прекрасно!", 8},
                {"Хорошо!", 9},
                {"Ура!", 10},
                {"Ок", 5},
            };

            string randomText = GetRandomText(content);

            // Устанавливаем текст кнопки
            okButton.Content = randomText;
            okButton.Width = CalculateButtonWidth(randomText, okButton);
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
                DragMove();
            }
        }

        /// <summary>
        /// Для закрытия кастомного заголовка
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void CloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        #endregion

        private void okButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void donateButton_Click(object sender, RoutedEventArgs e)
        {
            WriteReport.JokeReport();
            CommonOperations.EmailJoke();

            MessageBox.Show("Вы только что пожертвовали 10% своей зарплаты в пользу разработчика!\n" +
                            "Благодарю Вас от всего сердца!", "Категорически приветствую!", 
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>
        /// Выбор случайного текста с весами, без повторов
        /// </summary>
        /// <param name="content"></param>
        /// <returns></returns>
        private string GetRandomText(Dictionary<string, int> content)
        {
            // Исключаем последний текст
            var available = content
                .Where(x => x.Key != _lastText)
                .ToList();

            int totalWeight = available.Sum(x => x.Value);
            int roll = _random.Next(totalWeight);

            int cumulative = 0;
            foreach (var item in available)
            {
                cumulative += item.Value;
                if (roll < cumulative)
                {
                    _lastText = item.Key;
                    return item.Key;
                }
            }

            return available.Last().Key;
        }

        /// <summary>
        /// Авто-расчёт ширины кнопки по тексту
        /// </summary>
        /// <param name="text"></param>
        /// <param name="button"></param>
        /// <returns></returns>
        private double CalculateButtonWidth(string text, Button button)
        {
            var formattedText = new FormattedText(
                text,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface(button.FontFamily, button.FontStyle, button.FontWeight, button.FontStretch),
                button.FontSize,
                Brushes.Black,
                VisualTreeHelper.GetDpi(button).PixelsPerDip);

            // + padding и небольшой запас
            return formattedText.Width + 20;
        }

    }
}
