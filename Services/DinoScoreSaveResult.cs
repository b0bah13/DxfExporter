using System;

namespace Dino.Services
{
    public sealed class DinoScoreSaveResult
    {
        public bool Saved { get; init; }

        public bool IsNewUser { get; init; }

        public bool BeatPersonalRecord { get; init; }

        public bool IsOverallLeader { get; init; }

        public int PreviousHighestScore { get; init; }

        public int CurrentHighestScore { get; init; }

        public int TimeSeconds { get; init; }
    }
}
