using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DxfExporter.Constants
{
    public static class MaskConst
    {
        public const string Стандарт = "Стандарт";
        public const string Материал = "Материал";
        public const string Пользовательские = "Пользовательские";

        /// <summary>
        /// добавляем одно публичное свойство
        /// </summary>
        public static IReadOnlyList<string> AllFields { get; } = new[]
        {
            Стандарт,
            Материал,
            Пользовательские
        };
    }
}
