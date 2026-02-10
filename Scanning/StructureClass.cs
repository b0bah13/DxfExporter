using System.ComponentModel;
using System.Drawing;
using System.Runtime.CompilerServices;

namespace DxfExporter.Scanning
{
    /// <summary>
    /// Структура отсканированного файла.
    /// </summary>
    public class StructureClass : INotifyPropertyChanged
    {
        /// <summary>
        /// Флаг указывающий нужно ли выгрузить dxf
        /// </summary>
        private bool _needUnload = true;

        /// <summary>
        /// Обозначение элемента.
        /// </summary>
        public string PartNumber { get; set; }

        /// <summary>
        /// Наименование элемента.
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Полный путь до файла.
        /// </summary>
        public string Path { get; set; }
        
        /// <summary>
        /// Материал
        /// </summary>
        public string _material { get; set;}

        /// <summary>
        /// Дополнительный материал
        /// </summary>
        //public List<string> AddMaterial { get; set; } = new List<string>();

        /// <summary>
        /// Толщина
        /// </summary>
        public double Thickness { get; set; }
        
        /// <summary>
        /// Количество
        /// </summary>
        public double Quantity { get; set; }
        
        /// <summary>
        /// Видимое имя детали
        /// </summary>
        public string DisplayName { get; set; }

        /// <summary>
        /// Свойства выгрузки
        /// </summary>
        public string _unloadProp { get; set; } = string.Empty;

        /// <summary>
        /// Статус
        /// </summary>
        public string _status { get; set; } = string.Empty;

        /// <summary>
        /// Нужна гравировка? 
        /// </summary>
        public bool NeedGrav { get; set; } = true;

        /// <summary>
        /// Нужны линии гиба?
        /// </summary>
        public bool NeedBendLine { get; set; } = false;

        /// <summary>
        /// Выгрузка в шаблон
        /// </summary>
        public bool UnloadInTemplate { get; set; } = false;

        /// <summary>
        /// Деталь параметрическая
        /// </summary>
        public bool IsIPart { get; set; } = false;

        /// <summary>
        /// Деталь через состояния
        /// </summary>
        public bool IsModelStatePart { get; set; } = false;
        
        /// <summary>
        /// Имя исполнения/состояния
        /// </summary>
        public string MemberName { get; set; } = string.Empty;

        /// <summary>
        /// Выгрузить все исполнения?
        /// </summary>
        public bool UnloadAllVers { get; set; } = false;

        /// <summary>
        /// Проверка есть ли развёртка
        /// </summary>
        public bool NoFlat { get; set; } = false;

        /// <summary>
        /// Пустая развёртка
        /// </summary>
        public bool NullFlat { get; set; } = false;

        /// <summary>
        /// Развёртка превышающая размер листа
        /// </summary>
        public bool BigFlat { get; set; } = false;

        /// <summary>
        /// Проверка реальная толщина у детали или нет
        /// </summary>
        public bool FakeThickness { get; set; } = false;

        /// <summary>
        /// Проверка сочетания толщины и материала
        /// </summary>
        public bool ErrorMatThick { get; set; } = false;
        
        /// <summary>
        /// Изменение строки статус
        /// </summary>
        public string Status
        {
            get => _status;
            set
            {
                if (_status != value)
                {
                    _status = value;
                    OnPropertyChanged(nameof(Status));
                }
            }
        }

        /// <summary>
        /// Изменение строки материал
        /// </summary>
        public string Material
        {
            get => _material;
            set
            {
                if (_material != value)
                {
                    _material = value;
                    OnPropertyChanged(nameof(Material));
                }
            }
        }

        /// <summary>
        /// Изменение строки св-ва выгрузки
        /// </summary>
        public string UnloadProp
        {
            get => _unloadProp;
            set
            {
                if (_unloadProp != value)
                {
                    _unloadProp = value;
                    OnPropertyChanged(nameof(UnloadProp));
                }
            }
        }

        /// <summary>
        /// Изменение состояния галки для отображения в WPF
        /// </summary>
        public bool NeedUnload
        {
            get => _needUnload;
            set
            {
                if (_needUnload != value)
                {
                    _needUnload = value;
                    OnPropertyChanged(nameof(NeedUnload));
                }
            }
        }

        private System.Windows.Media.Brush _rowColor = System.Windows.Media.Brushes.White;//.Transparent;
        public System.Windows.Media.Brush RowColor
        {
            get => _rowColor;
            set
            {
                _rowColor = value;
                OnPropertyChanged(nameof(RowColor));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// Обновляет свойство UnloadProp на основе текущих флагов
        /// Вызывать после любого изменения флагов выгрузки
        /// </summary>
        public void UpdateUnloadProp()
        {
            string newValue = GetUnloadPropertiesText();
            if (UnloadProp != newValue)
            {
                UnloadProp = newValue;
            }
        }

        /// <summary>
        /// Пересчитывает и возвращает текстовое представление свойств выгрузки
        /// </summary>
        public string GetUnloadPropertiesText()
        {
            var parts = new List<string>();

            if (NeedGrav) parts.Add("Грав.");
            if (NeedBendLine) parts.Add("Гиб");
            if (UnloadInTemplate) parts.Add("Шаблон");

            return parts.Count switch
            {
                0 => "-",
                1 => parts[0],
                _ => string.Join("; ", parts)
            };
        }

        /// <summary>
        /// Создаёт полную копию объекта StructureClass с независимыми коллекциями
        /// </summary>
        /// <returns>Новая копия объекта</returns>
        public StructureClass Clone()
        {
            var clone = (StructureClass)this.MemberwiseClone();

            // Делаем независимый список дополнительных материалов
            //clone.AddMaterial = this.AddMaterial != null ? new List<string>(this.AddMaterial) : new List<string>();

            return clone;
        }
    }
}
