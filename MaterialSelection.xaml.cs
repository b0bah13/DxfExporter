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
    /// Логика взаимодействия для MaterialSelection.xaml
    /// </summary>
    public partial class MaterialSelection : Window
    {
        /// <summary>
        /// Список материалов, который отображается в окне
        /// </summary>
        public List<string> Materials { get; }

        /// <summary>
        /// Текст-инструкция для пользователя
        /// </summary>
        public string InfoText { get; }

        /// <summary>
        /// Выбранный пользователем материал (null, если ничего не выбрано или отмена)
        /// </summary>
        public List<string>? SelectedMaterials { get; private set; }

        /// <summary>
        /// Режим выбора (Single / Multiple)
        /// </summary>
        public SelectionMode SelectionMode { get; }
        

        public MaterialSelection(List<string> materialList, string info, SelectionMode mode)
        { 
            InitializeComponent();

            Materials = materialList;
            InfoText = info;
            SelectionMode = mode;

            infoText.Text = InfoText;
            materialListBox.SelectionMode = SelectionMode;
            materialListBox.ItemsSource = Materials;

            //materialListBox.SelectedItems.Cast<string>().ToList()
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
            if (materialListBox.SelectedItem is string selected)
            {
                SelectedMaterials = materialListBox.SelectedItems.Cast<string>().ToList();
                DialogResult = true;
            }
            else
            {
                // ничего не выбрано → можно оставить DialogResult = false или null
                DialogResult = false;
            }

            Close();
        }

        // закрывать по двойному клику
        private void materialListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (SelectionMode == SelectionMode.Multiple) return;
            if (materialListBox.SelectedItem != null)
            {
                okButton_Click(sender, e);
            }
        }
    }
}
