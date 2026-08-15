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

        public int Count
        {
            get
            {
                lock (_sync) return _pages.Count;
            }
        }

        public void Register(HtmlUiPage page)
        {
            if (page == null) throw new ArgumentNullException(nameof(page));

            lock (_sync)
            {
                if (_pages.ContainsKey(page.Id))
                    throw new InvalidOperationException("Page already registered: " + page.Id);

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
                var wasOpen = false;
                lock (_sync)
                {
                    if (!_pages.TryGetValue(id, out page)) return false;
                    wasOpen = string.Equals(_openId, id, StringComparison.OrdinalIgnoreCase);
                    _pages.Remove(id);
                    if (wasOpen) _openId = null;
                }

                if (wasOpen)
                {
                    try { page.Closed?.Invoke(); }
                    catch (Exception ex) { HtmlUiLogger.Error("Page close callback failed: " + id, ex); }

                    try
                    {
                        _host.State.Set("framework.page.lifecycle", new
                        {
                            state = "closed",
                            pageId = page.Id,
                            ownerId = page.OwnerId ?? ""
                        });
                        _host.SendEvent("framework.page.lifecycle", new
                        {
                            state = "closed",
                            pageId = page.Id,
                            ownerId = page.OwnerId ?? ""
                        });
                    }
                    catch (Exception ex)
                    {
                        HtmlUiLogger.Error("Failed to publish page closed lifecycle: " + id, ex);
                    }

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
                {
                    if (string.Equals(pair.Value.OwnerId, ownerId, StringComparison.OrdinalIgnoreCase))
                        ids.Add(pair.Key);
                }
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

                try
                {
                    _host.ValidatePage(page);
                }
                catch (Exception ex)
                {
                    HtmlUiLogger.Error("Page validation failed: " + id, ex);
                    return false;
                }

                HtmlUiLogger.Info("Page open requested: " + id + ", hostReady=" + _host.IsWebViewReady + ", currentBefore=" + (CurrentId ?? "<null>"));
                CloseCurrent();
                lock (_sync) _openId = page.Id;
                try
                {
                    _host.State.Set("framework.page.lifecycle", new
                    {
                        state = "opening",
                        pageId = page.Id,
                        ownerId = page.OwnerId,
                        path = page.RelativePath
                    });
                    _host.SendEvent("framework.page.lifecycle", new
                    {
                        state = "opening",
                        pageId = page.Id,
                        ownerId = page.OwnerId,
                        path = page.RelativePath
                    });
                }
                catch (Exception ex) { HtmlUiLogger.Error("Failed to publish page opening lifecycle: " + page.Id, ex); }

                try
                {
                    _host.Navigate(page);
                    _host.SetInputMode(page.DefaultInputMode);
                    try { page.Opened?.Invoke(); }
                    catch (Exception ex) { HtmlUiLogger.Error("Page open callback failed: " + page.Id, ex); }
                }
                catch
                {
                    lock (_sync)
                    {
                        if (string.Equals(_openId, page.Id, StringComparison.OrdinalIgnoreCase))
                            _openId = null;
                    }

                    try
                    {
                        _host.State.Set("framework.page.lifecycle", new
                        {
                            state = "closed",
                            pageId = page.Id,
                            ownerId = page.OwnerId ?? ""
                        });
                    }
                    catch { }

                    _host.Hide();
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
                if (string.Equals(_openId, id, StringComparison.OrdinalIgnoreCase)) CloseCurrent();
            }
        }

        public void CloseCurrent()
        {
            lock (_transitionSync)
            {
                string openId;
                HtmlUiPage page = null;
                lock (_sync)
                {
                    openId = _openId;
                    if (openId == null)
                    {
                        HtmlUiLogger.Info("Page CloseCurrent ignored: no open page.");
                        return;
                    }
                    _pages.TryGetValue(openId, out page);
                    _openId = null;
                }

                HtmlUiLogger.Info("Page CloseCurrent executing: page=" + openId + ", resolvedPage=" + (page == null ? "<null>" : page.Id));

                if (page != null)
                {
                    try { page.Closed?.Invoke(); }
                    catch (Exception ex) { HtmlUiLogger.Error("Page close callback failed: " + openId, ex); }
                }

                try
                {
                    _host.State.Set("framework.page.lifecycle", new
                    {
                        state = "closed",
                        pageId = openId,
                        ownerId = page == null ? "" : page.OwnerId
                    });
                    _host.SendEvent("framework.page.lifecycle", new
                    {
                        state = "closed",
                        pageId = openId,
                        ownerId = page == null ? "" : page.OwnerId
                    });
                }
                catch (Exception ex) { HtmlUiLogger.Error("Failed to publish page closed lifecycle: " + openId, ex); }
                HtmlUiLogger.Info("Page closed: " + openId);
                _host.Hide();
                HtmlUiLogger.Info("Page CloseCurrent finished: page=" + openId + ", currentAfter=" + (CurrentId ?? "<null>") + ", hostVisible=" + _host.IsVisible + ", inputMode=" + _host.InputMode);
            }
        }

        public bool Reload()
        {
            HtmlUiPage current;
            lock (_sync)
            {
                if (_openId == null || !_pages.TryGetValue(_openId, out current)) return false;
            }

            if (_host == null || !_host.IsHostCreated) return false;
            _host.Reload();
            return true;
        }

        public string CurrentId
        {
            get
            {
                lock (_sync) return _openId;
            }
        }

        public HtmlUiPage Current
        {
            get
            {
                lock (_sync)
                {
                    if (_openId == null) return null;
                    return _pages.TryGetValue(_openId, out var page) ? page : null;
                }
            }
        }

        public IEnumerable<HtmlUiPage> All
        {
            get
            {
                lock (_sync) return new List<HtmlUiPage>(_pages.Values);
            }
        }
    }
}
