using System.Text;

namespace DxfExporter.Versions
{
    /// <summary>
    /// Класс для хранения и получения истории версий программы.
    /// Данные хардкодятся внутри для простоты и отсутствия внешних зависимостей.
    /// </summary>
    public static class VersionHistory
    {
        // Приватный список версий.
        private static readonly List<VersionInfo> _versions = new List<VersionInfo>
        {
            new VersionInfo("1.0.1.1",
                "Добавлены кнопки:\n" +
                "'Добавить' - добавляет в таблицу открытые в inventor листовые детали\n" +
                "'Выбрать' - добавляет в таблицу выбранные пользователем файлы из папки\n" +
                "'Очистить' - очищает таблицу\n" +
                "'Открыть' - открывает папку с выгруженными DXF файлами\n" +
                "\nВ контекстное меню таблицы (ПКМ) добавлено:\n" +
                "'Оставить выбранные' - опция выбора строк\n" +
                "'Открыть деталь' - открывает выбранные детали\n" +
                "\nДобавлена проверка деталей при сканировании (можно отключить в настройках):\n" +
                "-проверка сочетания материала и толщины\n" +
                "-проверка правильности толщины (реальная из модели и выбранная из правила развёртывания)\n" +
                "-проверка поместится ли развёртка в лист металла (проверка в горизонте и вертикали)",
                new DateTime(2026,02,10)),
            new VersionInfo("1.0.1.2",
                "При выборе пользовательского расположения папки DXF сама папка 'DXF' создаваться не будет.\n" +
                "Для каждого пользователя сохраняются выбранные им настройки программы.\n" +
                "Добавлена кнопка 'Сбросить' - восстанавливает базовые настройки программы.",
                new DateTime(2026,02,14)),
            new VersionInfo("1.0.2.0",
                "На вкладке настройки добавлен пункт 'Сканировать все исполнения детали'.\n" +
                "Когда он включен сканируются все исполнения/состояния детали и выводятся в таблицу.\n" +
                "Таким образом можно выгрузить все исполнения детали, либо какие то выборочно.",
                new DateTime(2026,02,19)),
            new VersionInfo("1.0.3.0",
                "Добавлен значок загрузки при добавлении открытых деталей.\n" +
                "Устранена ошибка при которой добавлялись фантомные детали.\n" +
                "Устранена ошибка при которой листовые детали не из металла отображались с предупреждением.\n" +
                "В таблицу добавлен столбец с видом развёртки. Заполняется после выгрузки DXF файла.",
                new DateTime(2026,03,02)),
        };

        /// <summary>
        /// Возвращает информацию о конкретной версии по её номеру.
        /// </summary>
        /// <param name="versionNumber">Номер версии, например "1.2.0"</param>
        /// <returns>Объект VersionInfo или null, если версия не найдена.</returns>
        public static VersionInfo? GetByVersion(string versionNumber)
        {
            if (string.IsNullOrWhiteSpace(versionNumber))
                return null;

            return _versions.FirstOrDefault(v =>
                string.Equals(v.VersionNumber, versionNumber.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Возвращает форматированную строку с описанием одной версии.
        /// </summary>
        /// <param name="versionNumber">Номер версии</param>
        /// <returns>Строка вида "Версия 1.2.0 (20.01.2025):\nОписание..." или сообщение об ошибке</returns>
        public static string GetSingleVersionText(string versionNumber)
        {
            var version = GetByVersion(versionNumber);
            if (version == null)
            {
                return $"Версия {versionNumber} не найдена.";
            }

            return $"Версия {version.VersionNumber} ({version.ReleaseDate.ToShortDateString()}):\n" +
                   $"{version.Description}";
        }


        /// <summary>
        /// Возвращает полную историю всех версий в виде строки.
        /// </summary>
        public static string GetFullHistory()
        {
            StringBuilder sb = new StringBuilder();
            foreach (var version in _versions.OrderByDescending(v => v.ReleaseDate))
            {
                sb.AppendLine($"Версия {version.VersionNumber} ({version.ReleaseDate.ToShortDateString()}):");
                sb.AppendLine(version.Description);
                sb.AppendLine();
            }
            return sb.ToString();
        }

        /// <summary>
        /// Возвращает последнюю версию программы.
        /// </summary>
        public static VersionInfo? GetLatest()
        {
            return _versions.OrderByDescending(v => v.ReleaseDate).FirstOrDefault();
        }
    }
}
