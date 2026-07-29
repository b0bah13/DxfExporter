using System;

namespace Dino.Services
{
    public class GameOverInfo
    {
        /// <summary>
        /// Лучший результат (highestScore) на момент GameOver.
        /// </summary>
        public int HighestScore { get; init; }

        /// <summary>
        /// Длительность текущего забега в секундах.
        /// </summary>
        public int TimeSeconds { get; init; }
    }
}
