using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Inventor;
using OfficeOpenXml;
using Color = System.Drawing.Color;

namespace DxfExporter.Export_Dxf
{
    /// <summary>
    /// Класс для загрузки и кэширования данных из Excel-файла.
    /// </summary>
    public class ExcelDataLoader
    {
        private static ExcelDataLoader _instance;
        private readonly string _pathTable;
        private readonly List<ListStructure> _structureList;

        private class ListStructure
        {
            public string FullName { get; set; } = String.Empty;
            public string DrawName { get; set; } = String.Empty;
            public string Material { get; set; } = String.Empty;
        }

        /// <summary>
        /// Приватный конструктор для Singleton.
        /// </summary>
        /// <param name="pathTable">Путь к Excel-файлу с таблицей соответствия.</param>
        private ExcelDataLoader(string pathTable)
        {
            _pathTable = pathTable ?? throw new ArgumentNullException(nameof(pathTable));
            _structureList = LoadDataFromExcel();
        }

        /// <summary>
        /// Получает единственный экземпляр класса (Singleton).
        /// </summary>
        /// <param name="pathTable">Путь к Excel-файлу с таблицей соответствия.</param>
        /// <returns>Экземпляр ExcelDataLoader.</returns>
        public static ExcelDataLoader GetInstance(string pathTable)
        {
            return _instance ??= new ExcelDataLoader(pathTable);
        }

        /// <summary>
        /// Загружает данные из Excel-файла
        /// </summary>
        /// <returns>Структура с данными из Excel</returns>
        private List<ListStructure> LoadDataFromExcel()
        {
            var structure = new List<ListStructure>();

            // Указываем лицензию для EPPlus 8+
            ExcelPackage.License.SetNonCommercialOrganization("Моя организация");

            try
            {
                // Открываем файл
                using (var package = new ExcelPackage(new FileInfo(_pathTable), true))
                {
                    // Получаем лист "Основной материал"
                    var worksheet = package.Workbook.Worksheets["Основной материал"];
                    if (worksheet == null) { throw new Exception("Лист 'Основной материал' не найден в файле."); }

                    var Range = GetRowIndex(worksheet,"Лист");

                    for (int i = Range.StartRow; i <= Range.EndRow; i++)
                    {
                        // Получаем значения ячеек
                        var fullNameCell = worksheet.Cells[i, 3]; // Столбец C (3)
                        var nameCell = worksheet.Cells[i, 4]; // Столбец D (4)
                        var materialCell = worksheet.Cells[i, 5]; // Столбец E (5)

                        // Проверяем цвет в столбце C, пропускаем красный
                        var fontColorStr = fullNameCell.Style.Fill.BackgroundColor.LookupColor();
                        Color fontColor = ParseLookupColor(fontColorStr);
                        if (fontColor.ToArgb() == Color.Red.ToArgb())
                        {
                            continue;
                        }
                        
                        structure.Add(new ListStructure()
                        {
                            FullName = fullNameCell.Value.ToString(),
                            DrawName = nameCell.Value.ToString(),
                            Material = materialCell.Value.ToString()
                        });
                    }
                    
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка при загрузке данных из Excel: {ex.Message}");
                throw;
            }

            return structure;
        }

        /// <summary>
        /// Метод для получения диапазона обработки
        /// </summary>
        /// <returns></returns>
        private (int StartRow, int EndRow) GetRowIndex(ExcelWorksheet worksheet, string HeadingName)
        {
            int row = 3;
            int startRow = 0;
            int endRow = 0;

            while (true)
            {
                // Если ячейка B и D пустая, прекращаем чтение
                if (string.IsNullOrWhiteSpace(worksheet.Cells[row, 2]?.Value?.ToString())
                    && string.IsNullOrWhiteSpace(worksheet.Cells[row, 4]?.Value?.ToString()))
                {
                    endRow = row-1;
                    break;
                }

                if (worksheet.Cells[row, 2].Merge && worksheet.Cells[row, 2]?.Value?.ToString() == "Лист")
                {
                    startRow = row+1;
                }

                row++;
            }

            return (startRow, endRow);
        }

        /// <summary>
        /// Преобразует строку цвета (из EPPlus LookupColor) в System.Drawing.Color.
        /// Поддерживает форматы:
        /// - "#FFFF0000" (AARRGGBB с альфой)
        /// - "#FF0000"   (RRGGBB без альфы)
        /// - "Red"       (именованный цвет)
        /// - "255,0,0"   (RGB через запятую)
        /// </summary>
        public static Color ParseLookupColor(string lookup)
        {
            if (string.IsNullOrWhiteSpace(lookup))
                return Color.Empty;

            lookup = lookup.Trim();

            // --- Формат "#AARRGGBB" или "#RRGGBB" ---
            if (lookup.StartsWith("#"))
            {
                string hex = lookup.Substring(1);

                // Если 8 символов (AARRGGBB) — убираем альфу
                if (hex.Length == 8)
                    hex = hex.Substring(2);

                // теперь точно RRGGBB
                if (hex.Length == 6)
                {
                    int r = int.Parse(hex.Substring(0, 2), NumberStyles.HexNumber);
                    int g = int.Parse(hex.Substring(2, 2), NumberStyles.HexNumber);
                    int b = int.Parse(hex.Substring(4, 2), NumberStyles.HexNumber);
                    return Color.FromArgb(r, g, b);
                }
            }

            // --- Формат "R,G,B" ---
            if (lookup.Contains(","))
            {
                var parts = lookup.Split(',');
                if (parts.Length >= 3 &&
                    int.TryParse(parts[0], out int r) &&
                    int.TryParse(parts[1], out int g) &&
                    int.TryParse(parts[2], out int b))
                {
                    return Color.FromArgb(r, g, b);
                }
            }

            // --- Попытка интерпретировать как имя цвета ---
            Color named = Color.FromName(lookup);
            if (named.IsKnownColor || named.IsNamedColor)
                return named;

            // --- Попробуем как HTML (#RRGGBB или "Red") ---
            try
            {
                return ColorTranslator.FromHtml(lookup);
            }
            catch
            {
                // если не получилось — возвращаем пустой
                return Color.Empty;
            }
        }

        /// <summary>
        /// Получает из данных лист с нужным материалом и толщиной. 
        /// </summary>
        /// <param name="checkMaterial"></param>
        /// <returns>Возвращает размеры листа</returns>
        public (double ListWidth, double ListLength) GetDataFromPathTable(string checkMaterial,double thickness)
        {
            double listWidth = 0.0;
            double listLength = 0.0;
            
            foreach (var pair in _structureList
                         .Where(pair => checkMaterial.ToLower().Trim() == pair.Material.ToLower().Trim()))
            {
                // Универсальное регулярное выражение для Лист и Часть листа
                Regex regexUniversal = new Regex(
                    @"^(?:Лист\s+(\d+(?:[.,]\d+)?)\s*мм|Часть\s+листа\s+(\d+)\s*x)",
                    RegexOptions.Compiled);

                // Использование:
                Match m = regexUniversal.Match(pair.DrawName);
                if(!m.Success) continue;

                string thick = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
                // thick будет: "0,8", "1", "10", "45", "25", "40", "50" и т.д.
                double thickMm = double.Parse(thick.Replace(',', '.'), CultureInfo.InvariantCulture);

                if (thickness != thickMm) continue;
                Regex rxSize = new Regex(
                    @"[хx]\s*(\d+)\s*[хx]\s*(\d+)",
                    RegexOptions.IgnoreCase | RegexOptions.Compiled);

                Match match = rxSize.Match(pair.FullName);
                if (!match.Success) continue;

                listWidth = double.Parse(match.Groups[1].Value);
                listLength = double.Parse(match.Groups[2].Value);
            }

            return (listWidth, listLength);
        }
    }
}
