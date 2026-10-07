using Nsi.Client;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace DxfExporter.Export_Dxf
{
    /// <summary>
    /// Класс для загрузки и кэширования данных из Nsi.
    /// </summary>
    public class NsiDataLoader
    {
        private const string JsonPath = @"K:\Автоматизация процессов\Nsi\access.json";

        private static NsiDataLoader _instance;
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
        private NsiDataLoader()
        {
            _structureList = LoadDataFromNsi();
        }

        /// <summary>
        /// Получает единственный экземпляр класса (Singleton).
        /// </summary>
        /// <returns>Экземпляр NsiDataLoader.</returns>
        public static NsiDataLoader GetInstance()
        {
            return _instance ??= new NsiDataLoader();
        }

        /// <summary>
        /// Загружает данные из Nsi
        /// </summary>
        /// <returns>Структура с данными из Excel</returns>
        private List<ListStructure> LoadDataFromNsi()
        {
            var structure = new List<ListStructure>();

            // Подключение к NSI вместо таблицы
            var configuration = NsiConfiguration.Load(JsonPath);
            using var client = new NsiClient(configuration);
            var filter = new NsiViewFilter
            {
                Group = "Лист",
                ActiveOnly = true
            };
            var materials = client.GetAllMainMaterialsAsync(filter).GetAwaiter().GetResult();
            
            foreach (NsiKbItem material in materials)
            {
                structure.Add(new ListStructure()
                {
                    FullName = material.ErpName ?? "",
                    DrawName = material.DrawingDesignation ?? "",
                    Material = material.Material ?? ""
                });
            }
            
            return structure;
        }

        /// <summary>
        /// Получает из данных лист с нужным материалом и толщиной. 
        /// </summary>
        /// <param name="checkMaterial"></param>
        /// <returns>Возвращает размеры листа</returns>
        public (double ListWidth, double ListLength) GetDataFromNsi(string checkMaterial, double thickness, string baseMat)
        {
            double listWidth = 0.0;
            double listLength = 0.0;

            Regex rxThickness = new Regex(
                @"^Лист\s+(\d+(?:[.,]\d+)?)\s*мм",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

            Regex rxSimpleSheet = new Regex(
                @"^Лист\s+\d+(?:[.,]\d+)?\s*мм\s*$",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

            Regex rxSize = new Regex(
                @"[хx]\s*(\d+)\s*[хx]\s*(\d+)",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

            bool baseMatIsSheet = baseMat.Contains("лист", StringComparison.OrdinalIgnoreCase);

            foreach (var pair in _structureList
                         .Where(pair => string.Equals(checkMaterial.Trim(),
                         pair.Material.Trim(),StringComparison.OrdinalIgnoreCase)))
            {
                if (baseMatIsSheet)
                {
                    if (!string.Equals(pair.DrawName.Trim(), baseMat.Trim(), StringComparison.OrdinalIgnoreCase)) 
                        continue;
                }
                else
                {
                    if (!rxSimpleSheet.IsMatch(pair.DrawName))
                        continue;
                }

                Match thicknessMatch = rxThickness.Match(pair.DrawName);
                if (!thicknessMatch.Success)
                    continue;

                double thickMm = double.Parse(
                    thicknessMatch.Groups[1].Value.Replace(',', '.'),
                    CultureInfo.InvariantCulture);

                if (thickness != thickMm) continue;

                Match match = rxSize.Match(pair.FullName);
                if (!match.Success) continue;

                listWidth = double.Parse(match.Groups[1].Value);
                listLength = double.Parse(match.Groups[2].Value);

                break;
            }

            return (listWidth, listLength);
        }
    }
}
