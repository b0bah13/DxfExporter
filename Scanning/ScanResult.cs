using System.Collections.ObjectModel;

namespace DxfExporter.Scanning
{
    /// <summary>
    /// Главный класс для хранения данных после сканирования.
    /// </summary>
    public class ScanResult
    {
        public ObservableCollection<StructureClass> ScannedData { get; set; } = new ObservableCollection<StructureClass>(); // Список элементов структуры

        /// <summary>
        /// Добавляет новый узел в структуру сборки.
        /// </summary>
        public void AddNode(StructureClass node)
        {
            if (node != null && ScannedData.All(n => n.Path != node.Path))
            {
                ScannedData.Add(node);
            }
            
        }

        /// <summary>
        /// Очистка данных.
        /// </summary>
        public void ClearData()
        {
            ScannedData.Clear();
        }
    }
}
