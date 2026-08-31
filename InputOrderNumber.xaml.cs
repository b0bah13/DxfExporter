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

namespace DxfExporter
{
    /// <summary>
    /// Логика взаимодействия для InputOrderNumber.xaml
    /// </summary>
    public partial class InputOrderNumber : Window
    {
        /// <summary>
        /// Номер заказа
        /// </summary>
        public string OrderNumber { get; private set; }

        public InputOrderNumber()
        {
            InitializeComponent();
            tbInputText.Focus();
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
            OrderProcess();
        }
        
        private void tbInputText_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                OrderProcess();
            }
        }

        /// <summary>
        /// Ограничение ввода текста - только цифры
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void tbInputText_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Удаляем все недопустимые символы при вставке
            string allowed = "0123456789 ";
            string filtered = new string(tbInputText.Text.Where(c => allowed.Contains(c)).ToArray());

            if (tbInputText.Text != filtered)
            {
                // Сохраняем позицию курсора
                int cursorPos = tbInputText.SelectionStart;
                tbInputText.Text = filtered;
                tbInputText.SelectionStart = Math.Min(cursorPos, filtered.Length);
            }
        }

        private void OrderProcess()
        {
            string order = tbInputText.Text;
            if (!string.IsNullOrEmpty(order))
            {
                OrderNumber = order;
                DialogResult = true;
            }
            else
            {
                MessageBox.Show("Не введён номер заказа", "Нет номера",
                    MessageBoxButton.OK, MessageBoxImage.Stop);
                DialogResult = false;
            }

            Close();
        }
    }
}
