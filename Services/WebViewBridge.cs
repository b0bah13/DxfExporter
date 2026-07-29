using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Dino.Services
{
    public sealed class WebViewBridge : IDisposable
    {
        private readonly WebView2 _webView;

        private bool _initialized;

        public event EventHandler<string>? MessageReceived;

        public WebViewBridge(WebView2 webView)
        {
            _webView = webView;
        }


        public async Task InitializeAsync()
        {
            if (_initialized)
                return;
            
            var environment = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: WebView2LoaderHelper.UserDataFolder);

            await _webView.EnsureCoreWebView2Async(environment);

            _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

            _initialized = true;
        }


        public Task<string> ExecuteAsync(string script)
        {
            if (!_initialized)
                throw new InvalidOperationException("WebViewBridge is not initialized.");

            return _webView.CoreWebView2.ExecuteScriptAsync(script);
        }


        private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            MessageReceived?.Invoke(this, e.WebMessageAsJson);
        }


        public void Dispose()
        {
            if (!_initialized)
                return;

            _webView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;

            _initialized = false;
        }
    }
}
