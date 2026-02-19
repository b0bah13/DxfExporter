using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Inventor;

namespace DxfExporter.Scanning
{
    /// <summary>
    /// Структура для подсчёта компонентов
    /// </summary>
    public class OccStructure
    {
        /// <summary>
        /// Кол-во
        /// </summary>
        public double Quantity { get; set; }

        /// <summary>
        /// Путь до файла
        /// </summary>
        public string Path { get; set; }
        
        /// <summary>
        /// Компонент
        /// </summary>
        public ComponentOccurrence Occurrence { get; set; }

        /// <summary>
        /// Имя исполнения/состояния.
        /// </summary>
        public string MemberName { get; set; } = string.Empty;

    }
}
