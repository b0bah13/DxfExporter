using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DxfExporter.Constants
{
    public static class HeaderConst
    {
        public const string Обозначение = "Обозначение";
        public const string Наименование = "Наименование";
        public const string Материал = "Материал";
        public const string Толщина = "Толщина";
        public const string Количество = "Кол-во";

        /// <summary>
        /// добавляем одно публичное свойство
        /// </summary>
        public static IReadOnlyList<string> AllFields { get; } = new[]
        {
            Обозначение,
            Наименование,
            Материал,
            Толщина,
            Количество
        };
    }
}
