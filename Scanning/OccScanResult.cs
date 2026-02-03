using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DxfExporter.Scanning
{
    /// <summary>
    /// Результат сканирования компонентов
    /// </summary>
    public class OccScanResult
    {
        public ObservableCollection<OccStructure> ScannedOcc { get; set; } = new ObservableCollection<OccStructure>(); // Список элементов структуры

        /// <summary>
        /// Добавляет новый элемент
        /// </summary>
        public void AddNode(OccStructure node)
        {
            if (node == null) { return; }

            var existing = ScannedOcc.FirstOrDefault(x => x.Path == node.Path);

            if (existing != null)
            {
                existing.Quantity += 1;
            }
            else
            {
                ScannedOcc.Add(node);
            }
        }

        /// <summary>
        /// Очистка данных.
        /// </summary>
        public void ClearData()
        {
            ScannedOcc.Clear();
        }
    }
}
