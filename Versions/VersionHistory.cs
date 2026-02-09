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
            new VersionInfo("1.0.1.0",
                "Добавлены кнопки:\n" +
                "'Добавить' - добавляет в таблицу открытые в inventor листовые детали\n" +
                "'Выбрать' - добавляет в таблицу выбранные пользователем файлы из папки\n" +
                "'Очистить' - очищает таблицу\n" +
                "'Открыть' - открывает папку с выгруженными DXF файлами\n" +
                "\nВ контекстное меню таблицы (ПКМ) добавлена опция выбора - 'Оставить выбранные'",
                new DateTime(2026,02,09)),
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
