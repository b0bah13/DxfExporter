using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace Dino.Services
{
    public sealed class DinoGameController : IDisposable
    {
        private readonly DinoApi _api;
        private GameOverInfo? _bestSessionResult;

        public event EventHandler? Ready;

        public event EventHandler<GameOverInfo>? GameOver;

        /// <summary>
        /// Лучший highestScore за текущую сессию окна (не пишется в файл до закрытия).
        /// </summary>
        public GameOverInfo? BestSessionResult => _bestSessionResult;

        public DinoGameController(DinoApi api)
        {
            _api = api;
            _api.EventReceived += Api_EventReceived;
        }

        private void Api_EventReceived(object? sender, DinoEventArgs e)
        {
            switch (e.Event)
            {
                case "Ready":
                    Ready?.Invoke(this, EventArgs.Empty);
                    break;

                case "GameOver":
                    HandleGameOver(e);
                    break;
            }
        }

        private void HandleGameOver(DinoEventArgs e)
        {
            var info = new GameOverInfo
            {
                HighestScore = ReadInt(e.Data, "highestScore"),
                TimeSeconds = ReadInt(e.Data, "timeSeconds")
            };

            // Запоминаем только улучшение highestScore в рамках сессии.
            // Время сохраняем от того забега, который обновил рекорд.
            if (_bestSessionResult is null ||
                info.HighestScore > _bestSessionResult.HighestScore)
            {
                _bestSessionResult = info;
            }

            GameOver?.Invoke(this, info);
        }

        /// <summary>
        /// Подхватить текущий счёт и время из живого состояния игры.
        /// Вызывать при закрытии окна, чтобы не потерять результат незавершённого забега.
        /// </summary>
        public void CaptureCurrentScore(GameState state)
        {
            if (state is not { Score: > 0 })
                return;

            if (_bestSessionResult is null || state.Score > _bestSessionResult.HighestScore)
            {
                _bestSessionResult = new GameOverInfo
                {
                    HighestScore = state.Score,
                    TimeSeconds = state.TimeSeconds
                };
            }
        }

        private static int ReadInt(JsonElement data, string propertyName)
        {
            if (data.ValueKind != JsonValueKind.Object)
                return 0;

            if (!data.TryGetProperty(propertyName, out var element))
                return 0;

            return element.ValueKind switch
            {
                JsonValueKind.Number when element.TryGetInt32(out var i) => i,
                JsonValueKind.Number => (int)element.GetDouble(),
                JsonValueKind.String when int.TryParse(element.GetString(), out var s) => s,
                _ => 0
            };
        }

        public Task<GameState> GetStateAsync()
            => _api.GetStateAsync();

        public Task JumpAsync()
            => _api.JumpAsync();

        public Task RestartAsync()
            => _api.RestartAsync();

        public Task PauseAsync()
            => _api.PauseAsync();

        public Task ResumeAsync()
            => _api.ResumeAsync();

        public Task SetSpeedAsync(double speed)
            => _api.SetSpeedAsync(speed);

        public void Dispose()
        {
            _api.EventReceived -= Api_EventReceived;
        }
    }
}