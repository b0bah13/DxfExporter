using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.DirectoryServices.AccountManagement;
using System.Windows.Controls;

namespace DxfExporter.Versions
{
    internal class ShowNewVersion
    {
        // Путь до папки с отчётами
        private const string DirectPath = @"K:\Автоматизация процессов\Report\Обновления";
        
        /// <summary>
        /// Проверяет, нужно ли показать уведомление об обновлении пользователю.
        /// </summary>
        /// <returns>
        /// (NeedToShow, UnseenVersions) — 
        /// true + список версий, которые пользователь ещё не видел
        /// </returns>
        public static (bool NeedToShow, List<string> UnseenVersions) CheckNeedShow()
        {
            try
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                var progVer = assembly.GetName().Version;
                string currentVersion = progVer.ToString();

                // Для отладочной версии 1.0.0.0 ничего не показываем
                if (currentVersion == "1.0.0.0")
                    return (false, new List<string>());

                string userName = GetUserName().Trim();
                string fileName = $"{assembly.GetName().Name}.csv";
                string fullPath = Path.Combine(DirectPath, fileName);

                // Читаем файл
                List<string> lines = File.Exists(fullPath)
                    ? File.ReadAllLines(fullPath, Encoding.UTF8).ToList()
                    : new List<string>();

                // Парсим в таблицу (List<List<string>>)
                List<List<string>> table = lines
                    .Where(l => !string.IsNullOrWhiteSpace(l))
                    .Select(l => l.Split(';').Select(c => c.Trim()).ToList())
                    .ToList();

                // Если файл пустой — создаём первую строку с текущей версией
                if (table.Count == 0)
                {
                    table.Add(new List<string> { currentVersion });
                }

                List<string> versions = table[0];

                // Добавляем текущую версию, если её ещё нет
                if (!versions.Contains(currentVersion))
                {
                    versions.Add(currentVersion);

                    // Добавляем пустую ячейку во все существующие строки пользователей
                    for (int i = 1; i < table.Count; i++)
                    {
                        table[i].Add(string.Empty);
                    }
                }

                versions = table[0]; // обновляем на случай добавления

                // Строим структуру версия, список пользователей
                Dictionary<string, HashSet<string>> versionToUsers = new(StringComparer.OrdinalIgnoreCase);
                
                for (int col = 0; col < versions.Count; col++)
                {
                    string ver = versions[col];
                    var users = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    for (int row = 1; row < table.Count; row++)
                    {
                        if (col < table[row].Count)
                        {
                            string u = table[row][col];
                            if (!string.IsNullOrEmpty(u))
                                users.Add(u);
                        }
                    }
                    versionToUsers[ver] = users;
                }

                // проверка
                List<string> unseenVersions = versions
                    .Where(ver => !versionToUsers[ver].Contains(userName))
                    .ToList();

                bool needToShow = unseenVersions.Count > 0;

                // если нужно показать, то добавить пользователя в столбцы
                if (needToShow)
                {
                    bool fileChanged = false;

                    for (int col = 0; col < versions.Count; col++)
                    {
                        string ver = versions[col];

                        if (versionToUsers[ver].Contains(userName))
                            continue; // уже есть

                        // Ищем свободную ячейку в этом столбце
                        bool added = false;
                        for (int row = 1; row < table.Count; row++)
                        {
                            if (col < table[row].Count && string.IsNullOrEmpty(table[row][col]))
                            {
                                table[row][col] = userName;
                                added = true;
                                fileChanged = true;
                                break;
                            }
                        }

                        // Если свободных ячеек нет — добавляем новую строку
                        if (!added)
                        {
                            var newRow = new List<string>(new string[versions.Count]);
                            newRow[col] = userName;
                            table.Add(newRow);
                            fileChanged = true;
                        }

                        versionToUsers[ver].Add(userName);
                    }

                    // Сохраняем файл только если что-то менялось
                    if (fileChanged)
                    {
                        var newLines = table.Select(row => string.Join(";", row)).ToList();
                        File.WriteAllLines(fullPath, newLines, Encoding.UTF8);
                    }
                }

                return (needToShow, unseenVersions);
            }
            catch (Exception e)
            {
                Debug.WriteLine($"CheckNeedShow error: {e}");
                return (false, new List<string>());
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
        public static void ShowInfo(string progVer, string textInfo)
        {
            //var progVer = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            var wpfWin = new InfoWindow($"Программа обновлена до версии: {progVer}", textInfo);
            wpfWin.ShowDialog();
        }

    }
}
