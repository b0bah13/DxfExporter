using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DxfExporter.Constants
{
    class ErrorsConst
    {
        public const string NoFlat = "Нет развёртки!";
        public const string NullFlat = "Пустая развёртка!";
        public const string BigFlat = "Не влезет в лист!";
        public const string FakeThickness = "Проверьте толщину!";
        public const string ErrorMatThick = "Нет такого сочетания толщины и материала!";
        public const string DxfIntersections = "Проверьте пересечение линий в dxf";
        public const string ErrorCreateDxf = "Не удалось создать dxf";
        public const string LibraryFile = "Файл из библиотеки";
    }

}
