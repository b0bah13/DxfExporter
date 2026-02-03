using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace DxfExporter.MaskProcess
{
    /// <summary>
    /// Элемент в левом списке (доступные заголовки)
    /// </summary>
    public class HeaderItem : INotifyPropertyChanged
    {
        private string _key;
        private bool _isUsed;

        public string Key
        {
            get => _key;
            set { _key = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Флаг: элемент уже добавлен в правый список (маска)
        /// </summary>
        public bool IsUsed
        {
            get => _isUsed;
            set
            {
                if (_isUsed == value) return;
                _isUsed = value;
                OnPropertyChanged();
            }
        }

        public HeaderItem(string key)
        {
            Key = key;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
