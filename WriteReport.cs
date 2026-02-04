using System.Diagnostics;
using System.IO;

namespace DxfExporter
{
    internal class WriteReport
    {
        // Путь до папки с отчётами
        const string directPath = @"K:\Автоматизация процессов\Report\Auto generated reports";

        /// <summary>
        /// Создает CSV-файл с информацией о программе, пользователе и временем запуска.
        /// </summary>
        public static void StartWriteReport(string userName = null, string procedureName = null)
        {
            try
            {
                if (userName.Contains("Грахов")) { return; }

                // очищаем имя от -
                userName = userName.Split('-')[0];

                string programName = procedureName == null
                    ? System.Reflection.Assembly.GetExecutingAssembly().GetName().Name + ".csv"
                    : $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}-{procedureName}.csv";
                string reportName = Path.Combine(directPath, programName);

                using (StreamWriter writer = new StreamWriter(reportName, true, System.Text.Encoding.UTF8))
                {
                    writer.WriteLine($"{userName};{DateTime.Now.ToString("dd.MM.yy")};{DateTime.Now.ToString("HH:mm")}");
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
                //throw; //перебросить текущее исключение дальше по стеку вызовов
            }
        }

    }
}
