using System.Diagnostics;
using System.DirectoryServices.ActiveDirectory;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;

namespace DxfExporter
{
    public static class EnterpriseProtection
    {
        // Основная точка входа — вызывается из MainWindow
        public static bool ValidateAll()
        {
            string exePath = GetSafePath(Environment.GetCommandLineArgs()[0]);

            // Список проверок (добавляй/убирай по необходимости)
            var checks = new Func<bool>[]
            {
                () => CheckNetworkShare(@"\\10.0.20.242\Share"),
                () => CheckNetworkShare(@"\\10.0.20.31\Вентилятор\Подразделения\Центр"),
                () => CheckDomain(),                              // проверка, что ПК в домене предприятия
                //() => false
            };

            int noValid = 0;
            foreach (var check in checks)
            {
                try
                {
                    if (!check())
                    {
                        //ShowViolationAndSelfDelete(exePath);
                        noValid ++;
                        //return false;
                    }
                }
                catch
                {
                    // Если проверка упала с ошибкой — считаем нарушением
                    //ShowViolationAndSelfDelete(exePath);
                    noValid++;
                    //return false;
                }
            }
            
            //если все проверки провалились, только тогда false
            if (noValid != checks.Length) return true;
            ShowViolationAndSelfDelete(exePath);
            return false;

        }

        // Проверка сетевой папки (улучшенная версия)
        private static bool CheckNetworkShare(string sharePath)
        {
            if (string.IsNullOrEmpty(sharePath))
                return false;

            // Path.Exists иногда не срабатывает надёжно для сетевых путей → используем Directory.Exists + попытку доступа
            if (!Directory.Exists(sharePath))
                return false;

            try
            {
                // Дополнительно пытаемся получить список файлов — это надёжнее Path.Exists
                _ = Directory.GetDirectories(sharePath, "*", SearchOption.TopDirectoryOnly);
                return true;
            }
            catch
            {
                return false;
            }
        }

        // Проверка домена (компьютер должен быть в Active Directory домене предприятия)
        private static bool CheckDomain()
        {
            try
            {
                // Этот вызов бросит исключение, если компьютер НЕ в домене
                Domain domain = Domain.GetComputerDomain();

                // Дополнительно можно проверить имя домена (если хочешь строгое совпадение)
                string domainName = domain.Name.ToLowerInvariant();

                // Пример: если твой домен называется szemo.local или vent.local — замени
                if (!domainName.Contains("szemo"))
                {
                    return false; // чужой домен
                }

                return true;
            }
            catch (ActiveDirectoryObjectNotFoundException)
            {
                // Компьютер НЕ присоединён к домену
                return false;
            }
            catch
            {
                // Любая другая ошибка (например, нет прав) — считаем нарушением
                return false;
            }
        }

        // Показ сообщения + попытка самоудаления
        private static void ShowViolationAndSelfDelete(string exePath)
        {
            string? company = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyCompanyAttribute>()?
                .Company;

            MessageBox.Show(
                $"Вы пытаетесь запустить программу за пределами предприятия {company}, это недопустимо!",
                "Нарушение прав собственности!",
                MessageBoxButton.OK,
                MessageBoxImage.Stop);
            
            SelfDelete(exePath);

            Application.Current?.Shutdown(0);
            Environment.Exit(0);
        }

        // Самоудаление через bat-файл (универсально для всех версий .NET)
        private static void SelfDelete(string exePath)
        {
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
                return;

            try
            {
                int pid = Process.GetCurrentProcess().Id;

                string batchContent = $@"
@echo off
chcp 65001 > nul

set ""TARGET={exePath}""

:: Ждём завершения процесса
:loop
tasklist /FI ""PID eq {pid}"" | find ""{pid}"" > nul
if not errorlevel 1 (
    timeout /t 1 /nobreak > nul
    goto loop
)

:: Удаляем exe
del /f /q ""%TARGET%""

:: Если не удалился — пробуем ещё раз
if exist ""%TARGET%"" (
    timeout /t 2 /nobreak > nul
    del /f /q ""%TARGET%""
)

:: Удаляем батник
del /f /q ""%~f0""
";

                string tempBat = Path.Combine(Path.GetTempPath(), $"del_{Guid.NewGuid():N}.bat");

                //File.WriteAllText(tempBat, batchContent);
                // ВАЖНО: UTF-8 без BOM
                File.WriteAllText(tempBat, batchContent, new UTF8Encoding(false));

                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/C \"{tempBat}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
            }
            catch (Exception ex)
            {
                try
                {
                    File.AppendAllText(Path.Combine(Path.GetTempPath(), "protect_error.log"), ex.ToString() + "\n");
                }
                catch { }
            }
        }

        private static string GetSafePath(string path)
        {
            var shortPath = GetShortPath(path);

            // если вдруг short path не получен (редкий случай)
            if (string.IsNullOrWhiteSpace(shortPath))
                return path;

            return shortPath;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern int GetShortPathName(string lpszLongPath, StringBuilder lpszShortPath, int cchBuffer);

        private static string GetShortPath(string path)
        {
            var sb = new StringBuilder(260);
            GetShortPathName(path, sb, sb.Capacity);
            return sb.ToString();
        }


    }
}
