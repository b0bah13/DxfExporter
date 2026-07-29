using System;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Web.WebView2.Core;

namespace Dino.Services
{
    public class DinoApi : IDisposable
    {
        private readonly WebViewBridge _bridge;

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        public event EventHandler<DinoEventArgs>? EventReceived;

        public bool IsReady { get; private set; }

        public DinoApi(WebViewBridge bridge)
        {
            _bridge = bridge;
            _bridge.MessageReceived += OnWebMessageReceived;
        }

        private void OnWebMessageReceived(object? sender, string json)
        {
            var message = JsonSerializer.Deserialize<DinoMessage>(json, _jsonOptions);

            if (message == null)
                return;

            if (message.Event == "Ready")
                IsReady = true;

            EventReceived?.Invoke(this, new DinoEventArgs(message.Event, message.Data));
        }

        private Task<string> ExecuteAsync(string script)
        {
            return _bridge.ExecuteAsync(script);
        }

        private Task InvokeAsync(string method)
        {
            EnsureReady();
            return ExecuteAsync($"Game.{method}();");
        }

        private Task InvokeAsync(string method, object argument)
        {
            EnsureReady();

            string json = JsonSerializer.Serialize(argument);

            return ExecuteAsync($"Game.{method}({json});");
        }

        public Task StartAsync()
        {
            return InvokeAsync("start");
        }

        public Task RestartAsync()
        {
            return InvokeAsync("restart");
        }

        public Task PauseAsync()
        {
            return InvokeAsync("pause");
        }

        public Task ResumeAsync()
        {
            return InvokeAsync("resume");
        }

        public Task JumpAsync()
        {
            return InvokeAsync("jump");
        }

        public Task SetSpeedAsync(double speed)
        {
            return InvokeAsync("setSpeed", speed);
        }

        public async Task<GameState> GetStateAsync()
        {
            EnsureReady();

            string result = await ExecuteAsync("JSON.stringify(Game.getState());");
            string json = JsonSerializer.Deserialize<string>(result)!;

            return JsonSerializer.Deserialize<GameState>(json, _jsonOptions)!;
        }

        public Task ReadyTestAsync()
        {
            return ExecuteAsync("Bridge.send('ReadyTest');");
        }

        private void EnsureReady()
        {
            if (!IsReady)
                throw new InvalidOperationException("Game is not ready.");
        }

        public void Dispose()
        {
            _bridge.MessageReceived -= OnWebMessageReceived;
        }
    }

    public class DinoMessage
    {
        public string Event { get; set; } = "";

        public JsonElement Data { get; set; }
    }


    public class DinoEventArgs : EventArgs
    {
        public string Event { get; }

        public JsonElement Data { get; }


        public DinoEventArgs(string eventName, JsonElement data)
        {
            Event = eventName;
            Data = data;
        }
    }
}
