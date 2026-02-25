using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DxfExporter
{
    public class StateWithMessage
    {
        private readonly object _lock = new object();

        private bool _needExit;
        private string _message = string.Empty;
        private bool _fileError;
        private string _systemMessage = string.Empty;
        private string _userName = string.Empty;
        private int _userClickCount = 0;

        public bool NeedExit
        {
            get { lock (_lock) return _needExit; }
            set { lock (_lock) _needExit = value; }
        }

        public string Message
        {
            get { lock (_lock) return _message ?? string.Empty; }
            set { lock (_lock) _message = value ?? string.Empty; }
        }

        public bool FileError
        {
            get { lock (_lock) return _fileError; }
            set { lock (_lock) _fileError = value; }
        }

        public string SystemMessage
        {
            get { lock (_lock) return _systemMessage ?? string.Empty; }
            set { lock (_lock) _systemMessage = value ?? string.Empty; }
        }

        public string UserName
        {
            get { lock (_lock) return _userName ?? string.Empty; }
            set { lock (_lock) _userName = value ?? string.Empty; }
        }

        public int UserClickCount
        {
            get { lock (_lock) return _userClickCount; }
            set { lock (_lock) _userClickCount = value; }
        }

        /// <summary>
        /// Сбрасывает все поля в начальные значения
        /// </summary>
        public void ClearData()
        {
            lock (_lock)
            {
                _needExit = false;
                _fileError = false;
                _message = string.Empty;
                _systemMessage = string.Empty;
                _userName = string.Empty;
            }
        }
    }
}
