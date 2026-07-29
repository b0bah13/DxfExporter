using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Dino.Services
{
    /// <summary>
    /// Таблица результатов Dino в JSON-файле внутри savedFolder.
    /// Пользователи уникальны; запись обновляется только при улучшении highestScore.
    /// </summary>
    internal sealed class DinoScoreboardStore
    {
        private const string FileName = "dino-scores.json";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        private readonly string _filePath;

        public DinoScoreboardStore(string savedFolder)
        {
            if (string.IsNullOrWhiteSpace(savedFolder))
                throw new ArgumentException("GameFolder is required.", nameof(savedFolder));

            _filePath = Path.Combine(savedFolder, FileName);
        }

        public string FilePath => _filePath;

        public DinoScoreSaveResult SaveBestResult(string userName, int highestScore, int timeSeconds)
        {
            if (string.IsNullOrWhiteSpace(userName))
                throw new ArgumentException("User name is required.", nameof(userName));

            if (highestScore <= 0)
            {
                return new DinoScoreSaveResult
                {
                    Saved = false,
                    CurrentHighestScore = highestScore,
                    TimeSeconds = timeSeconds
                };
            }

            var scores = Load();
            var existing = scores.FirstOrDefault(entry =>
                string.Equals(entry.UserName, userName, StringComparison.OrdinalIgnoreCase));

            bool isNewUser = existing is null;
            int previousHighestScore = existing?.HighestScore ?? 0;
            bool beatPersonalRecord = false;
            bool saved = false;

            if (existing is null)
            {
                // Новый игрок — записываем только если есть положительный счёт
                if (highestScore > 0)
                {
                    scores.Add(new DinoScoreEntry
                    {
                        UserName = userName.Trim(),
                        HighestScore = highestScore,
                        TimeSeconds = timeSeconds
                    });
                    saved = true;
                }
                else
                {
                    return new DinoScoreSaveResult
                    {
                        Saved = false,
                        CurrentHighestScore = highestScore,
                        TimeSeconds = timeSeconds
                    };
                }
            }
            else
            {
                // Всегда добавляем время
                existing.TimeSeconds += timeSeconds;
                saved = true;

                // Рекорд обновляем только если побит
                if (highestScore > existing.HighestScore)
                {
                    previousHighestScore = existing.HighestScore;
                    existing.HighestScore = highestScore;
                    beatPersonalRecord = true;
                }
            }

            if (saved) Write(scores);

            int maxScore = scores.Count > 0 ? scores.Max(entry => entry.HighestScore) : 0;
            
            // "Больше всего очков" — единоличный максимум среди всей таблицы.
            bool isOverallLeader = highestScore > 0 &&
                                   highestScore == maxScore &&
                                   scores.Count(entry => entry.HighestScore == maxScore) == 1;

            return new DinoScoreSaveResult
            {
                Saved = saved,
                IsNewUser = isNewUser,
                // Личный рекорд показываем только если пользователь уже был в таблице.
                BeatPersonalRecord = beatPersonalRecord,
                IsOverallLeader = isOverallLeader,
                PreviousHighestScore = previousHighestScore,
                //CurrentHighestScore = highestScore,
                CurrentHighestScore = isNewUser || beatPersonalRecord ? highestScore : (existing?.HighestScore ?? highestScore),
                TimeSeconds = existing?.TimeSeconds ?? timeSeconds   // возвращаем уже суммарное время
            };
        }

        private List<DinoScoreEntry> Load()
        {
            if (!File.Exists(_filePath))
                return new List<DinoScoreEntry>();

            try
            {
                string json = File.ReadAllText(_filePath);

                if (string.IsNullOrWhiteSpace(json))
                    return new List<DinoScoreEntry>();

                return JsonSerializer.Deserialize<List<DinoScoreEntry>>(json, JsonOptions)
                       ?? new List<DinoScoreEntry>();
            }
            catch (JsonException)
            {
                // Битый файл не должен ломать закрытие игры — начинаем с пустой таблицы.
                return new List<DinoScoreEntry>();
            }
        }

        private void Write(List<DinoScoreEntry> scores)
        {
            string? directory = Path.GetDirectoryName(_filePath);

            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            var ordered = scores
                .OrderByDescending(entry => entry.HighestScore)
                .ThenBy(entry => entry.UserName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            string json = JsonSerializer.Serialize(ordered, JsonOptions);
            File.WriteAllText(_filePath, json, Encoding.UTF8);
        }
    }
}
