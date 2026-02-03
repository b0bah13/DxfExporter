using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.DirectoryServices.AccountManagement;

namespace DxfExporter.Versions
{
    internal class ShowNewVersion
    {
        // Путь до папки с отчётами
        private const string DirectPath = @"K:\Автоматизация процессов\Report\Обновления";

        /// <summary>
        /// Проверяет было ли показано уведомление у этого пользователя.
        /// </summary>
        public static bool CheckNeedShow()
        {
            try
            {
                var progVer = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                if (progVer.ToString() == "1.0.0.0") return false;
                string userName = GetUserName();
                string fileName = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name} {progVer}.csv";
                string fullPath = Path.Combine(DirectPath, fileName);
                
                // Читаем все строки файла (если файл не существует — получим пустой массив)
                string[] lines = File.Exists(fullPath)
                    ? File.ReadAllLines(fullPath, Encoding.UTF8)
                    : Array.Empty<string>();

                // Проверяем, есть ли уже такой пользователь 
                bool userAlreadyExists = lines
                    .Select(l => l.Trim())
                    .Contains(userName.Trim(), StringComparer.OrdinalIgnoreCase);

                if (userAlreadyExists)
                {
                    return false; // уже показывали — не нужно показывать снова
                }
                else
                {
                    // Добавляем пользователя в конец файла (файл будет создан автоматически, если его нет)
                    File.AppendAllLines(fullPath, new[] { userName }, Encoding.UTF8);
                    return true; // показываем уведомление впервые
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
                //throw; //перебросить текущее исключение дальше по стеку вызовов
                return false;
            }
        }

        /// <summary>
        /// <para>Получает имя сетевой учётной записи пользователя.</para>
        /// <para>Возвращает в формате "Иванов И.И."</para>
        /// <para>Если не удалось получить имя, возвращает доменное имя "invanov".</para>
        /// </summary>
        /// <returns>Строка с именем или доменным именем.</returns>
        public static string GetUserName()
        {
            string userName = string.Empty;

            using (var context = new PrincipalContext(ContextType.Domain))
            {
                var user = UserPrincipal.FindByIdentity(context, Environment.UserName);
                userName = user?.DisplayName ?? Environment.UserName;
                string[] nameParts = userName.Split(' ');

                if (nameParts.Length >= 3)
                {
                    return userName = $"{nameParts[0]} {nameParts[1][0]}.{nameParts[2][0]}.";
                }

            }

            return userName;
        }

        /// <summary>
        /// Показывает информацию о новой версии программы.
        /// </summary>
        /// <param name="textInfo">Текст сообщения</param>
        public static void ShowInfo(string textInfo)
        {
            var progVer = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;

            MessageBox.Show($"Что нового:\n{textInfo}", $"Программа обновлена до версии: {progVer}",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

    }
}
