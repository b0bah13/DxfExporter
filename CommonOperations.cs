using System.Diagnostics;
using System.DirectoryServices.AccountManagement;
using System.Runtime.InteropServices;
using Inventor;
using Outlook = Microsoft.Office.Interop.Outlook;
using System.IO;
using File = System.IO.File;
using Path = System.IO.Path;

namespace DxfExporter
{
    // Класс для глобальных методов
    internal class CommonOperations
    {
        public static string ProgramName = System.Reflection.Assembly.GetExecutingAssembly().GetName().Name; //имя программы
        public const string AdminEmailAdres = "grahov@szemospb.ru"; //почта админа
        private static readonly string _cooldownFilePath = Path.Combine(Path.GetTempPath(), "LastErrorEmailSent.txt");
        private static readonly TimeSpan _emailCooldown = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Словарь для замены запрещённых символов в параметрических деталях
        /// </summary>
        public static Dictionary<string, string> ReplaceDictionary = new Dictionary<string, string>()
        {
            {"/","(_FS)"}, {"*","(_AS)"}, {"?","(_QM)"}, {"\\","(_BS)"},
            {":","(_CO)"}, {"\"","(_DQ)"}, {"|","(_VB)"}, {"<","(_LB)"},
            {">","(_RB)"}, {";","(_SC)"},
        };

        /// <summary>
        /// Метод для отправки сообщение на почту при ошибке
        /// </summary>
        /// <param name="errorMessage"></param>
        /// <param name="errorTrace"></param>
        /// <param name="userName"></param>
        /// <param name="infoList"></param>
        public static void EmailOnError(string errorMessage, string errorTrace, string userName, List<string> infoList = null)
        {
            try
            {
                if (userName.Contains("Грахов")) { return; }
                
                if (IsCooldownActive()) { return; }
                
                Outlook.Application outlookApp = new Outlook.Application();
                Outlook.MailItem mailItem = (Outlook.MailItem)outlookApp.CreateItem(Outlook.OlItemType.olMailItem);

                // Получаем подпись по умолчанию для нового письма
                Outlook.Inspector inspector = mailItem.GetInspector;
                inspector.Display(); // Открываем письмо, чтобы подпись добавилась
                string signature = mailItem.HTMLBody;  // Получаем HTML с подписью
                                                       // Стиль для письма
                string emailStyle = "<style>p {font-family: calibri, sans-serif; font-size: 16px;}</style>";

                string info = null;
                if (infoList != null)
                {
                    //info = infoList.Aggregate(info, (current, s) => current + (s + "\n"));
                    // Склеиваем все строки с <br>
                    info = string.Join("<br>", infoList);
                    //info = string.Join("", infoList.Select(s => $"<li>{s}</li>"));
                    //info = $"<ul>{info}</ul>";
                }

                mailItem.To = AdminEmailAdres;
                mailItem.Subject = $"ErrorMessage for {ProgramName}";
                mailItem.HTMLBody = $@"
                            {emailStyle}
                            <p>Автоматическое сообщение об ошибке:<br><br>
                            Ошибка произошла в: {ProgramName}<br><br>
                            Пользователь: {userName}<br>
                            Информация: <br>{info}<br>                            
                            Сообщение ошибки: <br>{errorMessage}<br>
                            Стек вызова: <br>{errorTrace}<br><br>
                            </p>
                            {signature}";
                //В файле: {asmDoc.FullFileName}<br>

                mailItem.Display();
                mailItem.Send();

                // фиксируем время отправки
                //_lastErrorEmailSent = DateTime.UtcNow;
                UpdateLastErrorEmailTime();
            }
            catch (System.Exception ex)
            {
                Debug.WriteLine($"Error Message Not Sent. Please email the issue to {AdminEmailAdres}\nDetails: {ex.Message}");
            }
        }

        /// <summary>
        /// Проверка прошло ли 5 минут с момента отправки письма
        /// </summary>
        /// <returns></returns>
        private static bool IsCooldownActive()
        {
            try
            {
                if (!File.Exists(_cooldownFilePath))
                    return false;

                string text = File.ReadAllText(_cooldownFilePath).Trim();

                if (DateTime.TryParse(text, out DateTime lastSent))
                {
                    return DateTime.UtcNow - lastSent <= _emailCooldown;
                }
            }
            catch
            {
                // ignored
            }

            return false; // если что-то пошло не так — разрешаем отправить
        }

        /// <summary>
        /// Обновляем время в файле
        /// </summary>
        private static void UpdateLastErrorEmailTime()
        {
            try
            {
                File.WriteAllText(_cooldownFilePath, DateTime.UtcNow.ToString("o"));
            }
            catch
            {
                // ignored
            }
        }

        /// <summary>
        /// Подготовка отправки шуточного сообщения
        /// </summary>
        public static void EmailJoke()
        {
            try
            {
                Outlook.Application outlookApp = new Outlook.Application();
                Outlook.MailItem mailItem = (Outlook.MailItem)outlookApp.CreateItem(Outlook.OlItemType.olMailItem);

                // Получаем подпись по умолчанию для нового письма
                Outlook.Inspector inspector = mailItem.GetInspector;
                inspector.Display(); // Открываем письмо, чтобы подпись добавилась
                string signature = mailItem.HTMLBody;  // Получаем HTML с подписью
                                                       // Стиль для письма
                string emailStyle = "<style>p {font-family: calibri, sans-serif; font-size: 16px;}</style>";

                string info = null;

                mailItem.To = "";
                mailItem.Subject = "Изменения в зарплате";
                mailItem.HTMLBody = $@"
                            {emailStyle}
                            <p>Руководителю отдела.<br>
                            Прошу перечислить 5% от моей заработной платы за текущей месяц Грахову В.А.<br><br>
                            </p>
                            {signature}";
                //В файле: {asmDoc.FullFileName}<br>

                mailItem.Display();
                //mailItem.Send();
            }
            catch (System.Exception ex)
            {
                Debug.WriteLine($"Error Message Not Sent. Please email the issue to {AdminEmailAdres}\nDetails: {ex.Message}");
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
                var user = UserPrincipal.FindByIdentity(context, System.Environment.UserName);
                userName = user?.DisplayName ?? System.Environment.UserName;
                string[] nameParts = userName.Split(' ');

                if (nameParts.Length >= 3)
                {
                    return userName = $"{nameParts[0]} {nameParts[1][0]}.{nameParts[2][0]}.";
                }

            }

            return userName;
        }

        /// <summary>
        /// Возвращает PartDocument по полному пути: если открыт — берёт из Documents, иначе открывает.
        /// </summary>
        /// <param name="invApp">Экземпляр Inventor.Application.</param>
        /// <param name="fullPath">Полный путь к файлу.</param>
        /// <param name="wasOpened">True если документ был открыт этим методом.</param>
        /// <returns>PartDocument или null.</returns>
        public static PartDocument GetOrOpenPartDocument(Inventor.Application invApp, string fullPath, out bool wasOpened)
        {
            // Изначально считаем что не открывали
            wasOpened = false;

            // 1) Пытаемся найти документ среди уже открытых по FullFileName
            foreach (var doc in from Document doc in invApp.Documents
                     where doc.DocumentType == DocumentTypeEnum.kPartDocumentObject
                     where !string.IsNullOrWhiteSpace(doc.FullFileName)
                     where string.Equals(doc.FullFileName, fullPath, StringComparison.OrdinalIgnoreCase)
                     select doc)
            {
                return (PartDocument)doc;
            }

            // 2) Не нашли — открываем
            return NeedOpenedFile(invApp, fullPath, out wasOpened);
        }

        /// <summary>
        /// Возвращает PartDocument по полному пути: если открыт и видимый — берёт из Documents, иначе открывает.
        /// </summary>
        /// <param name="invApp">Экземпляр Inventor.Application.</param>
        /// <param name="fullPath">Полный путь к файлу.</param>
        /// <param name="wasOpened">True если документ был открыт этим методом.</param>
        /// <returns>PartDocument или null.</returns>
        public static PartDocument GetVisibleOrOpenPartDocument(Inventor.Application invApp, string fullPath, out bool wasOpened)
        {
            // Изначально считаем что не открывали
            wasOpened = false;

            // 1) Пытаемся найти документ среди уже открытых по FullFileName
            foreach (var doc in from Document doc in invApp.Documents.VisibleDocuments
                     where doc.DocumentType == DocumentTypeEnum.kPartDocumentObject
                     where !string.IsNullOrWhiteSpace(doc.FullFileName)
                     where string.Equals(doc.FullFileName, fullPath, StringComparison.OrdinalIgnoreCase)
                     select doc)
            {
                return (PartDocument)doc;
            }

            // 2) Не нашли — открываем
            return NeedOpenedFile(invApp, fullPath, out wasOpened);
        }

        /// <summary>
        /// Функция для открытия файла
        /// </summary>
        /// <param name="invApp"></param>
        /// <param name="fullPath"></param>
        /// <param name="wasOpened"></param>
        /// <returns></returns>
        public static PartDocument NeedOpenedFile(Inventor.Application invApp, string fullPath, out bool wasOpened)
        {
            PartDocument openedDoc = invApp.Documents.Open(fullPath, true) as PartDocument;
            wasOpened = openedDoc != null;

            return openedDoc;
        }

        /// <summary>
        /// Закрываем файлы, очищаем ресурсы.
        /// </summary>
        /// <param name="doc">Файл который нужно закрыть. Передаваем динамически</param>
        public static void ReleaseObject(dynamic doc)
        {
            try
            {
                doc?.Close();
                if (doc != null) Marshal.ReleaseComObject(doc);
            }
            catch (Exception e)
            {
                Debug.WriteLine(e.Message);
            }
        }
    }

}
