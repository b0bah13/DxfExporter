using DxfExporter;
using Microsoft.Office.Interop.Outlook;
using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;

namespace Dino.Services
{
    static class WebView2LoaderHelper
    {
        public static string WebView2Folder { get; private set; } = string.Empty;
        public static string UserDataFolder { get; private set; } = string.Empty;

        public static void EnsureLoader(string nameSpace)
        {
            // Имя ресурса обычно выглядит как "ИмяПроекта.WebView2Loader.dll"
            // или "ИмяПроекта.Native.WebView2Loader.dll"
            // Проверь точное имя:
            //foreach (var n in Assembly.GetExecutingAssembly().GetManifestResourceNames()) Debug.WriteLine(n);

            string temp = Path.GetTempPath();
            string prefix = $"{CommonOperations.ProgramName}_WebView2_";

            //очистка старых папок
            foreach (string dir in Directory.GetDirectories(temp, prefix + "*"))
            {
                try
                {
                    Directory.Delete(dir, true);
                }
                catch
                {
                    // Папка ещё занята другим процессом или нет доступа.
                    // Просто пропускаем.
                }
            }

            string resourceName = $"{nameSpace}.WebView2Loader.dll";

            WebView2Folder = Path.Combine(temp, prefix + Environment.ProcessId);
            UserDataFolder = Path.Combine(WebView2Folder, "UserData");
            
            Directory.CreateDirectory(WebView2Folder);
            string dllPath = Path.Combine(WebView2Folder, "WebView2Loader.dll");

            if (!File.Exists(dllPath))
            {
                using var stream = Assembly.GetExecutingAssembly()
                                       .GetManifestResourceStream(resourceName)
                                   ?? throw new InvalidOperationException($"Resource '{resourceName}' not found");

                using var fs = File.Create(dllPath);
                stream.CopyTo(fs);
            }
            else
            {
                return;
            }

            // Самое важное — указать путь ДО любых вызовов WebView2
            CoreWebView2Environment.SetLoaderDllFolderPath(WebView2Folder);
        }
    }
}
