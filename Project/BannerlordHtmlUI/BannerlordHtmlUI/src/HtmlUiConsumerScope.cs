using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BannerlordHtmlUI
{
    /// <summary>
    /// Owns all registrations made by one consumer Mod.
    /// Dispose() unregisters pages, commands, requests, and owned state keys.
    /// Page close callbacks run before the scope becomes finally disposed.
    /// </summary>
    public sealed class HtmlUiConsumerScope : IDisposable
    {
        private readonly object _sync = new object();
        private readonly List<string> _pageIds = new List<string>();
        private readonly List<string> _commandNames = new List<string>();
        private readonly List<string> _requestNames = new List<string>();
        private readonly List<string> _stateKeys = new List<string>();
        private readonly List<string> _contentRootIds = new List<string>();
        private bool _disposed;
        private bool _disposing;

        internal HtmlUiConsumerScope(string ownerId)
        {
            if (string.IsNullOrWhiteSpace(ownerId)) throw new ArgumentException("Owner id is required.", nameof(ownerId));
            OwnerId = ownerId;
        }

        public string OwnerId { get; }
        public bool IsDisposed
        {
            get { lock (_sync) return _disposed; }
        }

        public string RegisterContentRoot(string id, string directory)
        {
            lock (_sync)
            {
                ThrowIfDisposedOrDisposingLocked();
                var scopedId = HtmlUiService.MakeScopedName(OwnerId, id);
                HtmlUiService.RegisterContentRoot(scopedId, directory);
                _contentRootIds.Add(scopedId);
                return scopedId;
            }
        }

        public string RegisterPage(HtmlUiPage page)
        {
            if (page == null) throw new ArgumentNullException(nameof(page));

            lock (_sync)
            {
                ThrowIfDisposedOrDisposingLocked();

                var pageId = HtmlUiService.MakeScopedName(OwnerId, page.Id);
                var requestedContentRoot = string.IsNullOrWhiteSpace(page.ContentRootId) ? "ui" : page.ContentRootId;
                var contentRootId = string.Equals(requestedContentRoot, "framework", StringComparison.OrdinalIgnoreCase)
                    ? HtmlUiService.MakeScopedName(OwnerId, "ui")
                    : (requestedContentRoot.StartsWith(OwnerId + ".", StringComparison.OrdinalIgnoreCase)
                        ? requestedContentRoot
                        : HtmlUiService.MakeScopedName(OwnerId, requestedContentRoot));

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
        }

        public void RegisterCommand(string name, Action<JToken> handler)
        {
            lock (_sync)
            {
                ThrowIfDisposedOrDisposingLocked();
                var fullName = HtmlUiService.MakeScopedName(OwnerId, name);
                HtmlUiService.RegisterCommand(fullName, handler, OwnerId);
                _commandNames.Add(fullName);
            }
        }

        public void RegisterRequest(string name, Func<JToken, Task<object>> handler)
        {
            lock (_sync)
            {
                ThrowIfDisposedOrDisposingLocked();
                var fullName = HtmlUiService.MakeScopedName(OwnerId, name);
                HtmlUiService.RegisterRequest(fullName, handler, OwnerId);
                _requestNames.Add(fullName);
            }
        }

        public void RegisterRequest(string name, Func<JToken, CancellationToken, Task<object>> handler)
        {
            lock (_sync)
            {
                ThrowIfDisposedOrDisposingLocked();
                var fullName = HtmlUiService.MakeScopedName(OwnerId, name);
                HtmlUiService.RegisterRequest(fullName, handler, OwnerId);
                _requestNames.Add(fullName);
            }
        }

        public void SetState(string key, object value)
        {
            lock (_sync)
            {
                ThrowIfDisposedOrDisposingLocked();
                var fullKey = HtmlUiService.MakeScopedName(OwnerId, key);
                HtmlUiService.State.Set(fullKey, value);
                if (!_stateKeys.Exists(x => string.Equals(x, fullKey, StringComparison.OrdinalIgnoreCase)))
                    _stateKeys.Add(fullKey);
            }
        }

        public void RemoveState(string key)
        {
            lock (_sync)
            {
                ThrowIfDisposedOrDisposingLocked();
                var fullKey = HtmlUiService.MakeScopedName(OwnerId, key);
                HtmlUiService.State.Remove(fullKey);
                _stateKeys.RemoveAll(x => string.Equals(x, fullKey, StringComparison.OrdinalIgnoreCase));
            }
        }

        public void SendEvent(string name, object payload)
        {
            lock (_sync)
            {
                ThrowIfDisposedOrDisposingLocked();
                HtmlUiService.SendEvent(HtmlUiService.MakeScopedName(OwnerId, name), payload);
            }
        }

        public string ContentRootName(string id)
        {
            lock (_sync)
            {
                ThrowIfDisposedOrDisposingLocked();
                return HtmlUiService.MakeScopedName(OwnerId, id);
            }
        }

        public string ScopeName(string name)
        {
            lock (_sync)
            {
                ThrowIfDisposedOrDisposingLocked();
                return HtmlUiService.MakeScopedName(OwnerId, name);
            }
        }

        public void Dispose()
        {
            List<string> pageIds;
            List<string> commandNames;
            List<string> requestNames;
            List<string> stateKeys;
            List<string> contentRootIds;

            lock (_sync)
            {
                if (_disposed || _disposing) return;
                _disposing = true;

                pageIds = new List<string>(_pageIds);
                commandNames = new List<string>(_commandNames);
                requestNames = new List<string>(_requestNames);
                stateKeys = new List<string>(_stateKeys);
                contentRootIds = new List<string>(_contentRootIds);
            }

            try
            {
                if (!HtmlUiService.IsInitialized)
                {
                    lock (_sync) ClearOwnershipListsLocked();
                    HtmlUiLogger.Info("Consumer scope disposed after Framework shutdown: " + OwnerId);
                    return;
                }

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

                // Stop owner-owned handlers before unregistering them. This closes the
                // lifecycle gap where a cancellable request could otherwise continue
                // running after the consumer scope has been disposed.
                try
                {
                    HtmlUiBridge.Current?.CancelRequestsByOwner(OwnerId);
                }
                catch (Exception ex)
                {
                    HtmlUiLogger.Error("Consumer scope owner request cancellation failed: " + OwnerId, ex);
                }

                foreach (var pageId in pageIds)
                {
                    try { HtmlUiService.Pages.Unregister(pageId); }
                    catch (Exception ex) { HtmlUiLogger.Error("Consumer scope page cleanup failed: " + pageId, ex); }
                }

                foreach (var command in commandNames)
                {
                    try { HtmlUiService.UnregisterCommand(command, OwnerId); }
                    catch (Exception ex) { HtmlUiLogger.Error("Consumer scope command cleanup failed: " + command, ex); }
                }

                foreach (var request in requestNames)
                {
                    try { HtmlUiService.UnregisterRequest(request, OwnerId); }
                    catch (Exception ex) { HtmlUiLogger.Error("Consumer scope request cleanup failed: " + request, ex); }
                }

                lock (_sync) ClearOwnershipListsLocked();
                foreach (var key in stateKeys)
                {
                    try { HtmlUiService.State.Remove(key); }
                    catch (Exception ex) { HtmlUiLogger.Error("Consumer scope state cleanup failed: " + key, ex); }
                }

                foreach (var contentRootId in contentRootIds)
                {
                    try { HtmlUiService.Host.UnregisterContentRoot(contentRootId); }
                    catch (Exception ex) { HtmlUiLogger.Error("Consumer scope content root cleanup failed: " + contentRootId, ex); }
                }

                HtmlUiLogger.Info("Consumer scope disposed: " + OwnerId);
            }
            finally
            {
                lock (_sync)
                {
                    _disposing = false;
                    _disposed = true;
                }
            }
        }

        private void ClearOwnershipListsLocked()
        {
            _pageIds.Clear();
            _commandNames.Clear();
            _requestNames.Clear();
            _stateKeys.Clear();
            _contentRootIds.Clear();
        }

        private void ThrowIfDisposedOrDisposingLocked()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(HtmlUiConsumerScope));
            if (_disposing) throw new InvalidOperationException("HtmlUiConsumerScope is disposing.");
        }
    }
}
