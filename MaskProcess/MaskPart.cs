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
    /// Элемент маски в правом списке
    /// </summary>
    public class MaskPart : INotifyPropertyChanged
    {
        private string _text;
        public string Text
        {
            get => _text;
            set
            {
                if (_text == value) return;
                _text = value;
                OnPropertyChanged();
            }
        }

        public bool IsEditable { get; }

        public MaskPart(string text, bool isEditable = false)
        {
            Text = text ?? throw new ArgumentNullException(nameof(text));
            IsEditable = isEditable;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
