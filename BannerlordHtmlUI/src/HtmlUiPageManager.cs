using System;
using System.Collections.Generic;

namespace BannerlordHtmlUI
{
    public sealed class HtmlUiPageManager
    {
        private readonly Dictionary<string, HtmlUiPage> _pages = new Dictionary<string, HtmlUiPage>(StringComparer.OrdinalIgnoreCase);
        private readonly object _sync = new object();
        private readonly object _transitionSync = new object();
        private HtmlUiHost _host;
        private string _openId;

        internal void Attach(HtmlUiHost host) => _host = host;
        public int Count { get { lock (_sync) return _pages.Count; } }

        public void Register(HtmlUiPage page)
        {
            if (page == null) throw new ArgumentNullException(nameof(page));
            lock (_sync)
            {
                if (_pages.ContainsKey(page.Id)) throw new InvalidOperationException("Page already registered: " + page.Id);
                _pages.Add(page.Id, page);
            }
            HtmlUiLogger.Info("Page registered: " + page.Id + " -> " + page.ContentRootId + ":/" + page.RelativePath);
        }

        public bool Contains(string id)
        {
            if (id == null) return false;
            lock (_sync) return _pages.ContainsKey(id);
        }

        public bool Unregister(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return false;
            lock (_transitionSync)
            {
                HtmlUiPage page = null;
                bool wasOpen;
                lock (_sync)
                {
                    if (!_pages.TryGetValue(id, out page)) return false;
                    wasOpen = string.Equals(_openId, id, StringComparison.OrdinalIgnoreCase);
                    _pages.Remove(id);
                    if (wasOpen) _openId = null;
                }

                if (wasOpen)
                {
                    _host.ClearPendingNavigation();
                    InvokeClosed(page, id);
                    PublishClosed(id, page);
                    _host.SetInputMode(HtmlUiInputMode.Hidden);
                    _host.Hide();
                }
                HtmlUiLogger.Info("Page unregistered: " + id);
                return true;
            }
        }

        public int UnregisterByOwner(string ownerId)
        {
            if (string.IsNullOrWhiteSpace(ownerId)) return 0;
            List<string> ids;
            lock (_sync)
            {
                ids = new List<string>();
                foreach (var pair in _pages)
                    if (string.Equals(pair.Value.OwnerId, ownerId, StringComparison.OrdinalIgnoreCase)) ids.Add(pair.Key);
            }
            var count = 0;
            foreach (var id in ids) if (Unregister(id)) count++;
            return count;
        }

        public bool Open(string id)
        {
            if (_host == null) throw new InvalidOperationException("HTML UI page manager is not attached to a host.");
            if (string.IsNullOrWhiteSpace(id)) return false;

            lock (_transitionSync)
            {
                HtmlUiPage page;
                lock (_sync)
                {
                    if (!_pages.TryGetValue(id, out page))
                    {
                        HtmlUiLogger.Warn("Page open failed: page not registered: " + id);
                        return false;
                    }
                }

                try { _host.ValidatePage(page); }
                catch (Exception ex) { HtmlUiLogger.Error("Page validation failed: " + id, ex); return false; }

                HtmlUiLogger.Info("Page open requested: " + id + ", hostReady=" + _host.IsWebViewReady + ", currentBefore=" + (CurrentId ?? "<null>"));
                CloseCurrentInternal();
                _host.ClearPendingNavigation();

                lock (_sync) _openId = page.Id;
                try
                {
                    PublishOpening(page);
                    _host.Navigate(page);
                    _host.SetInputMode(page.DefaultInputMode);
                    try { page.Opened?.Invoke(); }
                    catch (Exception ex) { HtmlUiLogger.Error("Page open callback failed: " + page.Id, ex); }
                }
                catch (Exception ex)
                {
                    lock (_sync)
                    {
                        if (string.Equals(_openId, page.Id, StringComparison.OrdinalIgnoreCase)) _openId = null;
                    }
                    _host.ClearPendingNavigation();
                    PublishClosed(page.Id, page);
                    try { _host.SetInputMode(HtmlUiInputMode.Hidden); } catch { }
                    try { _host.Hide(); } catch { }
                    HtmlUiLogger.Error("Page open failed and was rolled back: " + page.Id, ex);
                    throw;
                }

                HtmlUiLogger.Info("Page open state committed: " + page.Id + ", inputMode=" + _host.InputMode + ", requestedVisible=" + _host.IsVisible);
                return true;
            }
        }

        public void Close(string id)
        {
            lock (_transitionSync)
            {
                HtmlUiLogger.Info("Page Close requested: id=" + (id ?? "<null>") + ", current=" + (CurrentId ?? "<null>"));
                if (string.Equals(_openId, id, StringComparison.OrdinalIgnoreCase)) CloseCurrentInternal();
            }
        }

        public void CloseCurrent()
        {
            lock (_transitionSync) CloseCurrentInternal();
        }

        private void CloseCurrentInternal()
        {
            string openId;
            HtmlUiPage page = null;
            lock (_sync)
            {
                openId = _openId;
                if (openId == null)
                {
                    HtmlUiLogger.Info("Page CloseCurrent ignored: no open page.");
                    try { _host.SetInputMode(HtmlUiInputMode.Hidden); } catch { }
                    _host.ClearPendingNavigation();
                    return;
                }
                _pages.TryGetValue(openId, out page);
                _openId = null;
            }

            HtmlUiLogger.Info("Page CloseCurrent executing: page=" + openId + ", resolvedPage=" + (page == null ? "<null>" : page.Id));
            _host.ClearPendingNavigation();
            InvokeClosed(page, openId);
            PublishClosed(openId, page);
            try { _host.SetInputMode(HtmlUiInputMode.Hidden); }
            catch (Exception ex) { HtmlUiLogger.Error("Failed to restore Hidden input mode while closing page: " + openId, ex); }
            try { _host.Hide(); }
            catch (Exception ex) { HtmlUiLogger.Error("Failed to hide HTML UI host while closing page: " + openId, ex); }

            HtmlUiLogger.Info("Page CloseCurrent finished: page=" + openId + ", currentAfter=" + (CurrentId ?? "<null>") + ", hostVisible=" + _host.IsVisible + ", inputMode=" + _host.InputMode);
        }

        private void InvokeClosed(HtmlUiPage page, string pageId)
        {
            if (page == null) return;
            try { page.Closed?.Invoke(); }
            catch (Exception ex) { HtmlUiLogger.Error("Page close callback failed: " + pageId, ex); }
        }

        private void PublishOpening(HtmlUiPage page)
        {
            try
            {
                var payload = new { state = "opening", pageId = page.Id, ownerId = page.OwnerId, path = page.RelativePath };
                _host.State.Set("framework.page.lifecycle", payload);
                _host.SendEvent("framework.page.lifecycle", payload);
            }
            catch (Exception ex) { HtmlUiLogger.Error("Failed to publish page opening lifecycle: " + page.Id, ex); }
        }

        private void PublishClosed(string pageId, HtmlUiPage page)
        {
            try
            {
                var payload = new { state = "closed", pageId = pageId, ownerId = page == null ? "" : (page.OwnerId ?? "") };
                _host.State.Set("framework.page.lifecycle", payload);
                _host.SendEvent("framework.page.lifecycle", payload);
            }
            catch (Exception ex) { HtmlUiLogger.Error("Failed to publish page closed lifecycle: " + pageId, ex); }
        }

        public bool Reload()
        {
            if (_host == null || !_host.IsHostCreated) return false;
            lock (_transitionSync)
            {
                HtmlUiPage current;
                lock (_sync)
                {
                    if (_openId == null || !_pages.TryGetValue(_openId, out current))
                    {
                        HtmlUiLogger.Info("Page Reload ignored: no open page.");
                        return false;
                    }
                }
                if (!_host.IsVisible || !_host.IsWebViewReady) return false;
                try
                {
                    var payload = new { state = "reloading", pageId = current.Id, ownerId = current.OwnerId, path = current.RelativePath };
                    _host.State.Set("framework.page.lifecycle", payload);
                    _host.SendEvent("framework.page.lifecycle", payload);
                }
                catch (Exception ex) { HtmlUiLogger.Error("Failed to publish page reloading lifecycle: " + current.Id, ex); }
                try { _host.Reload(); HtmlUiLogger.Info("Page reload requested: " + current.Id); return true; }
                catch (Exception ex) { HtmlUiLogger.Error("Page reload failed: " + current.Id, ex); return false; }
            }
        }

        public string CurrentId { get { lock (_sync) return _openId; } }
        public HtmlUiPage Current
        {
            get
            {
                lock (_sync)
                {
                    if (_openId == null) return null;
                    HtmlUiPage page;
                    return _pages.TryGetValue(_openId, out page) ? page : null;
                }
            }
        }
        public IEnumerable<HtmlUiPage> All { get { lock (_sync) return new List<HtmlUiPage>(_pages.Values); } }
    }
}
