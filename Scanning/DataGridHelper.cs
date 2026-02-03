using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DxfExporter.Scanning
{
    /// <summary>
    /// Вспомогательный класс (чтобы IsEnabled работал красиво)
    /// </summary>
    public static class DataGridHelper
    {
        public static bool GetHasIpartOrModelState(StructureClass item)
        {
            return item?.IsIPart == true || item?.IsModelStatePart == true;
        }
    }
}
