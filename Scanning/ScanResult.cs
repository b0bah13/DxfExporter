using System.Collections.ObjectModel;
using DxfExporter.Constants;

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
            if (node == null) {return;}

            var existing = ScannedData.Any(n => n.Path == node.Path && n.MemberName == node.MemberName);

            var existing2 = ScannedData.FirstOrDefault(x =>
                x.PartNumber == node.PartNumber &&
                x.Description == node.Description);
                //&&x.Material == node.Material && Equals(x.Thickness, node.Thickness));

            if (existing2 != null)
            {
                if (existing2.Path != node.Path)
                {
                    node.Duplicate = true;
                    existing2.Duplicate = true;
                }
            }

            if (!existing) //&& existing2 == null)
            {
                ScannedData.Add(node);
            }
            
            //if (existing2 != null)
            //{
            //    existing2.Quantity += node.Quantity;
            //}
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
