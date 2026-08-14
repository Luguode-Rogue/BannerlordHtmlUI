using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Threading.Tasks;

namespace BannerlordHtmlUI
{
    /// <summary>
    /// Owns all registrations made by one consumer Mod.
    /// Dispose() unregisters pages, commands, requests, and owned state keys.
    /// </summary>
    public sealed class HtmlUiConsumerScope : IDisposable
    {
        private readonly List<string> _pageIds = new List<string>();
        private readonly List<string> _commandNames = new List<string>();
        private readonly List<string> _requestNames = new List<string>();
        private readonly List<string> _stateKeys = new List<string>();
        private readonly List<string> _contentRootIds = new List<string>();
        private bool _disposed;

        internal HtmlUiConsumerScope(string ownerId)
        {
            if (string.IsNullOrWhiteSpace(ownerId)) throw new ArgumentException("Owner id is required.", nameof(ownerId));
            OwnerId = ownerId;
        }

        public string OwnerId { get; }
        public bool IsDisposed => _disposed;

        public string RegisterContentRoot(string id, string directory)
        {
            ThrowIfDisposed();
            var scopedId = HtmlUiService.MakeScopedName(OwnerId, id);
            HtmlUiService.RegisterContentRoot(scopedId, directory);
            _contentRootIds.Add(scopedId);
            return scopedId;
        }

        public string RegisterPage(HtmlUiPage page)
        {
            ThrowIfDisposed();
            if (page == null) throw new ArgumentNullException(nameof(page));

            var pageId = HtmlUiService.MakeScopedName(OwnerId, page.Id);
            var contentRootId = string.Equals(page.ContentRootId, "framework", StringComparison.OrdinalIgnoreCase)
                ? HtmlUiService.MakeScopedName(OwnerId, "ui")
                : (page.ContentRootId.StartsWith(OwnerId + ".", StringComparison.OrdinalIgnoreCase)
                    ? page.ContentRootId
                    : HtmlUiService.MakeScopedName(OwnerId, page.ContentRootId));

            var scopedPage = new HtmlUiPage(pageId, page.RelativePath)
            {
                ContentRootId = contentRootId,
                OwnerId = OwnerId,
                HotReload = page.HotReload,
                DefaultInputMode = page.DefaultInputMode,
                Opened = page.Opened,
                Closed = page.Closed
            };

            HtmlUiService.Pages.Register(scopedPage);
            _pageIds.Add(scopedPage.Id);
            return scopedPage.Id;
        }

        public void RegisterCommand(string name, Action<JToken> handler)
        {
            ThrowIfDisposed();
            var fullName = HtmlUiService.MakeScopedName(OwnerId, name);
            HtmlUiService.RegisterCommand(fullName, handler, OwnerId);
            _commandNames.Add(fullName);
        }

        public void RegisterRequest(string name, Func<JToken, Task<object>> handler)
        {
            ThrowIfDisposed();
            var fullName = HtmlUiService.MakeScopedName(OwnerId, name);
            HtmlUiService.RegisterRequest(fullName, handler, OwnerId);
            _requestNames.Add(fullName);
        }

        public void SetState(string key, object value)
        {
            ThrowIfDisposed();
            var fullKey = HtmlUiService.MakeScopedName(OwnerId, key);
            HtmlUiService.State.Set(fullKey, value);
            if (!_stateKeys.Exists(x => string.Equals(x, fullKey, StringComparison.OrdinalIgnoreCase))) _stateKeys.Add(fullKey);
        }

        public void SendEvent(string name, object payload)
        {
            ThrowIfDisposed();
            HtmlUiService.SendEvent(HtmlUiService.MakeScopedName(OwnerId, name), payload);
        }

        public string ContentRootName(string id)
        {
            ThrowIfDisposed();
            return HtmlUiService.MakeScopedName(OwnerId, id);
        }

        public string ScopeName(string name)
        {
            ThrowIfDisposed();
            return HtmlUiService.MakeScopedName(OwnerId, name);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // Framework shutdown may happen before a consumer Mod is unloaded.
            // In that case all framework-owned registrations are already gone, so
            // only finalize the local scope bookkeeping and avoid noisy cleanup errors.
            if (!HtmlUiService.IsInitialized)
            {
                _pageIds.Clear();
                _commandNames.Clear();
                _requestNames.Clear();
                _stateKeys.Clear();
                _contentRootIds.Clear();
                HtmlUiLogger.Info("Consumer scope disposed after Framework shutdown: " + OwnerId);
                return;
            }

            // Close the active page first so the WebView cannot keep navigating into a scope being removed.
            try
            {
                var current = HtmlUiService.Pages.Current;
                if (current != null && string.Equals(current.OwnerId, OwnerId, StringComparison.OrdinalIgnoreCase))
                    HtmlUiService.Pages.CloseCurrent();
            }
            catch (Exception ex)
            {
                HtmlUiLogger.Error("Consumer scope active-page cleanup failed: " + OwnerId, ex);
            }

            foreach (var pageId in _pageIds)
            {
                try { HtmlUiService.Pages.Unregister(pageId); }
                catch (Exception ex) { HtmlUiLogger.Error("Consumer scope page cleanup failed: " + pageId, ex); }
            }

            foreach (var command in _commandNames)
            {
                try { HtmlUiService.UnregisterCommand(command); }
                catch (Exception ex) { HtmlUiLogger.Error("Consumer scope command cleanup failed: " + command, ex); }
            }

            foreach (var request in _requestNames)
            {
                try { HtmlUiService.UnregisterRequest(request); }
                catch (Exception ex) { HtmlUiLogger.Error("Consumer scope request cleanup failed: " + request, ex); }
            }

            foreach (var key in _stateKeys)
            {
                try { HtmlUiService.State.Remove(key); }
                catch (Exception ex) { HtmlUiLogger.Error("Consumer scope state cleanup failed: " + key, ex); }
            }

            foreach (var contentRootId in _contentRootIds)
            {
                try { HtmlUiService.Host.UnregisterContentRoot(contentRootId); }
                catch (Exception ex) { HtmlUiLogger.Error("Consumer scope content root cleanup failed: " + contentRootId, ex); }
            }

            _pageIds.Clear();
            _commandNames.Clear();
            _requestNames.Clear();
            _stateKeys.Clear();
            _contentRootIds.Clear();

            HtmlUiLogger.Info("Consumer scope disposed: " + OwnerId);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException("HtmlUiConsumerScope");
        }
    }
}
