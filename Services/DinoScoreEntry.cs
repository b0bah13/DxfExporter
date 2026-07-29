using System;

namespace Dino.Services
{
    public sealed class DinoScoreEntry
    {
        public string UserName { get; set; } = "";

        public int HighestScore { get; set; }

        public int TimeSeconds { get; set; }
    }
}
