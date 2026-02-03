using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DxfExporter
{
    public class StateWithMessage_old
    {
        public bool NeedExit { get; set; } = false;
        public string Message { get; set; } = string.Empty;
        public bool FileError { get; set; } = false;
        public string SystemMessage { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;

        public void ClearData()
        {
            NeedExit = false;
            FileError = false;
            Message = "";
            SystemMessage = "";
            UserName = "";
        }
    }
}
