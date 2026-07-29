using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;


namespace Dino.Services
{
    internal sealed class DinoGameHost
    {
        private readonly WebView2 _webView;
        private readonly DinoGameSettings _settings;
        private readonly DinoScoreboardStore _scoreboard;
        private bool _initialized;
        private bool _started;
        private bool _resultsSaved;

        public WebViewBridge Bridge { get; private set; } = null!;

        public DinoApi Api { get; private set; } = null!;

        public DinoGameController Controller { get; private set; } = null!;

        public DinoGameSettings Settings => _settings;

        public DinoGameHost(WebView2 webView, DinoGameSettings settings)
        {
            _webView = webView;
            _settings = settings;
            _scoreboard = new DinoScoreboardStore(settings.SavedFolder);
        }

        public async Task<bool> InitializeAsync()
        {
            if (_initialized)
                return true;

            if (string.IsNullOrWhiteSpace(_settings.GameFolder))
            {
                Debug.WriteLine("[Dino] GameFolder is not specified.");
                return false;
            }

            if (!Directory.Exists(_settings.GameFolder))
            {
                Debug.WriteLine($"[Dino] Game folder not found: {_settings.GameFolder}");
                return false;
            }

            string indexPath = Path.Combine(_settings.GameFolder, "index.html");

            if (!File.Exists(indexPath))
            {
                Debug.WriteLine($"[Dino] index.html not found: {indexPath}");
                return false;
            }

            try
            {
                // Один bridge: подписка на WebMessageReceived живёт на этом экземпляре.
                Bridge = new WebViewBridge(_webView);
                await Bridge.InitializeAsync();

                _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "dino",
                    _settings.GameFolder,
                    CoreWebView2HostResourceAccessKind.Allow);

                Api = new DinoApi(Bridge);
                Controller = new DinoGameController(Api);

                _initialized = true;
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Dino] Initialization failed: {ex.Message}");
                return false;
            }
        }

        public void Start()
        {
            if (!_initialized)
                throw new InvalidOperationException("DinoGameHost is not initialized.");

            if (_started)
                return;

            // Навигацию запускаем только после того, как окно успело подписаться на события.
            _webView.Source = new Uri("https://dino/index.html");
            _started = true;
        }

        /// <summary>
        /// Сохраняет лучший результат сессии в JSON-таблицу GameFolder.
        /// Сначала запрашивает текущее состояние игры у JS, чтобы подхватить
        /// счёт незавершённого забега, а затем записывает итоговый BestSessionResult.
        /// </summary>
        public async Task<DinoScoreSaveResult?> SaveSessionResultsAsync()
        {
            if (_resultsSaved || Controller is null)
                return null;

            // Запросить актуальное состояние из JS, чтобы не потерять
            // результат текущего (ещё не завершённого) забега.
            try
            {
                var state = await Controller.GetStateAsync();
                Controller.CaptureCurrentScore(state);
            }
            catch (Exception ex)
            {
                // WebView2 может уже завершаться — используем то, что есть в BestSessionResult.
                Debug.WriteLine($"[Dino] Could not capture live state: {ex.Message}");
            }

            _resultsSaved = true;

            var best = Controller.BestSessionResult;

            if (best is null || best.HighestScore <= 0)
                return null;

            return _scoreboard.SaveBestResult(_settings.UserName, best.HighestScore, best.TimeSeconds);
        }

    }
}