namespace DxfExporter.Versions
{
    /// <summary>
    /// Представляет информацию о версии программы.
    /// </summary>
    public class VersionInfo
    {
        /// <summary>
        /// Номер версии (например, "1.0.0").
        /// </summary>
        public string VersionNumber { get; }

        /// <summary>
        /// Описание изменений в этой версии.
        /// </summary>
        public string Description { get; }

        /// <summary>
        /// Дата выпуска версии.
        /// </summary>
        public DateTime ReleaseDate { get; }

        /// <summary>
        /// Конструктор для создания экземпляра VersionInfo.
        /// </summary>
        /// <param name="versionNumber">Номер версии.</param>
        /// <param name="description">Описание изменений.</param>
        /// <param name="releaseDate">Дата выпуска.</param>
        public VersionInfo(string versionNumber, string description, DateTime releaseDate = default(DateTime))
        {
            VersionNumber = versionNumber ?? throw new ArgumentNullException(nameof(versionNumber));
            Description = description ?? throw new ArgumentNullException(nameof(description));
            ReleaseDate = releaseDate == default(DateTime) ? DateTime.Now : releaseDate;
        }
    }
}
