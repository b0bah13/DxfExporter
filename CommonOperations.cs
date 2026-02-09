using System.Diagnostics;
using System.Runtime.InteropServices;
using Inventor;
using Outlook = Microsoft.Office.Interop.Outlook;

namespace DxfExporter
{
    // Класс для глобальных методов
    internal class CommonOperations
    {
        public static string ProgramName = System.Reflection.Assembly.GetExecutingAssembly().GetName().Name; //имя программы
        public const string AdminEmailAdres = "grahov@szemospb.ru"; //почта админа
        
        // Метод для отправки сообщение на почту при ошибке
        public static void EmailOnError(string errorMessage, string errorTrace, string userName, List<string> infoList = null)
        {
            try
            {
                if (userName.Contains("Грахов")) { return; }

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
            }
            catch (System.Exception ex)
            {
                Debug.WriteLine($"Error Message Not Sent. Please email the issue to {AdminEmailAdres}\nDetails: {ex.Message}");
            }
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

    }

}
