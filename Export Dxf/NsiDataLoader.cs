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
                Group = "Лист"
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
        public (double ListWidth, double ListLength) GetDataFromNsi(string checkMaterial, double thickness)
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
                if (!m.Success) continue;

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
