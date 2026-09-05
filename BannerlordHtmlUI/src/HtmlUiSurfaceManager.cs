using System;

namespace BannerlordHtmlUI
{
    /// <summary>Read-only snapshot of a registered surface, safe to hand to consumers and diagnostics.</summary>
    public sealed class HtmlUiSurfaceView
    {
        public string Id { get; internal set; }
        public string OwnerId { get; internal set; }
        public string ContentRootId { get; internal set; }
        public string RelativePath { get; internal set; }
        public string Uri { get; internal set; }
        public int ZIndex { get; internal set; }
        public bool Visible { get; internal set; }
        public bool Enabled { get; internal set; }
        public bool Suppressed { get; internal set; }
        public HtmlUiInputMode InputDemand { get; internal set; }
    }

    /// <summary>Read-only snapshot of the aggregated surface layer.</summary>
    public sealed class HtmlUiSurfaceAggregate
    {
        public HtmlUiInputMode EffectiveInputMode { get; internal set; }
        public string InputOwnerId { get; internal set; }
        public bool HasVisible { get; internal set; }
    }

    /// <summary>
    /// Owns the lifecycle of parallel overlay surfaces. Registration is per owner scope;
    /// visibility and input demand are aggregated into a single effective input mode.
    /// Surfaces never touch the host directly: the input coordinator applies the result.
    /// </summary>
    public sealed class HtmlUiSurfaceManager
    {
        private sealed class Entry
        {
            public HtmlUiSurface Surface;
            public bool Visible;
            public bool Suppressed;
            public long Sequence;
        }

        private const string StateKey = "framework.surfaces";
        private const string AggregateStateKey = "framework.surfaces.aggregate";

        private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        private readonly object _sync = new object();
        private HtmlUiHost _host;
        private long _sequence;
        private bool _suppressAll;

        internal void Attach(HtmlUiHost host) => _host = host;

        /// <summary>Raised whenever the aggregate result changes. The input coordinator listens to this.</summary>
        internal event Action SurfacesChanged;

        public int Count { get { lock (_sync) return _entries.Count; } }

        /// <summary>Surfaces currently displayed: visible, not suppressed by a page, and enabled.</summary>
        public int VisibleCount
        {
            get
            {
                lock (_sync)
                {
                    var count = 0;
                    foreach (var pair in _entries) if (IsDisplayed(pair.Value)) count++;
                    return count;
                }
            }
        }

        /// <summary>True while the page-dominant policy is hiding every surface.</summary>
        internal bool IsSuppressedByPage { get { lock (_sync) return _suppressAll; } }

        public void Register(HtmlUiSurface surface)
        {
            if (surface == null) throw new ArgumentNullException(nameof(surface));
            if (_host == null) throw new InvalidOperationException("HTML UI surface manager is not attached to a host.");

            // Surfaces must be owned. A surface registered outside a consumer scope would never be
            // unregistered on module unload, so reject it with an actionable message instead of
            // silently leaking DOM, state and event subscriptions.
            if (string.IsNullOrWhiteSpace(surface.OwnerId) || string.Equals(surface.OwnerId, "framework", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "Surface '" + surface.Id + "' has no owner. Register it through HtmlUiService.CreateScope(ownerId).RegisterSurface(...) " +
                    "so it is scoped and disposed with your module.");

            _host.ValidateSurface(surface);

            lock (_sync)
            {
                if (_entries.ContainsKey(surface.Id))
                    throw new InvalidOperationException("Surface already registered: " + surface.Id + ". Unregister it or dispose its owner scope first.");

                _entries.Add(surface.Id, new Entry
                {
                    Surface = surface,
                    Visible = false,
                    Suppressed = _suppressAll,
                    Sequence = NextSequenceLocked()
                });
            }

            HtmlUiLogger.Info("Surface registered: " + surface.Id + " -> " + surface.ContentRootId + ":/" + surface.RelativePath);
            Publish();
            NotifyChanged();
        }

        public bool Contains(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return false;
            lock (_sync) return _entries.ContainsKey(id);
        }

        public bool Unregister(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return false;

            Entry entry;
            lock (_sync)
            {
                if (!_entries.TryGetValue(id, out entry)) return false;
                _entries.Remove(id);
            }

            bool wasDisplayed = IsDisplayed(entry);
            if (wasDisplayed) SafeInvokeClosed(entry.Surface, id);
            HtmlUiLogger.Info("Surface unregistered: " + id);
            Publish();
            NotifyChanged();
            return true;
        }

        public int UnregisterByOwner(string ownerId)
        {
            if (string.IsNullOrWhiteSpace(ownerId)) return 0;

            var ids = new System.Collections.Generic.List<string>();
            lock (_sync)
            {
                foreach (var pair in _entries)
                    if (string.Equals(pair.Value.Surface.OwnerId, ownerId, StringComparison.OrdinalIgnoreCase))
                        ids.Add(pair.Key);
            }

            var count = 0;
            foreach (var id in ids) if (Unregister(id)) count++;
            return count;
        }

        public bool Show(string id) => SetVisible(id, true);

        public bool Hide(string id) => SetVisible(id, false);

        public bool SetVisible(string id, bool visible)
        {
            Entry entry;
            bool wasDisplayed;
            bool nowDisplayed;
            lock (_sync)
            {
                if (!_entries.TryGetValue(id, out entry))
                {
                    HtmlUiLogger.Warn("Surface visibility change ignored: not registered: " + id);
                    return false;
                }
                if (entry.Visible == visible) return true;
                wasDisplayed = IsDisplayed(entry);
                entry.Visible = visible;
                if (visible) entry.Sequence = NextSequenceLocked();
                nowDisplayed = IsDisplayed(entry);
            }

            // Callbacks follow the display state, not the intent: a Show while suppressed by a
            // page must not report Opened, and the later restore must report it exactly once.
            if (!wasDisplayed && nowDisplayed) SafeInvokeOpened(entry.Surface, id);
            else if (wasDisplayed && !nowDisplayed) SafeInvokeClosed(entry.Surface, id);

            HtmlUiLogger.Info("Surface visibility: " + id + " -> " + visible);
            Publish();
            NotifyChanged();
            return true;
        }

        public bool SetZIndex(string id, int zIndex)
        {
            return Mutate(id, entry =>
            {
                if (entry.Surface.ZIndex == zIndex) return false;
                entry.Surface.ZIndex = zIndex;
                entry.Sequence = NextSequenceLocked();
                return true;
            });
        }

        public bool SetInputDemand(string id, HtmlUiInputMode mode)
        {
            return Mutate(id, entry =>
            {
                if (entry.Surface.InputDemand == mode) return false;
                entry.Surface.InputDemand = mode;
                entry.Sequence = NextSequenceLocked();
                return true;
            });
        }

        public bool SetEnabled(string id, bool enabled)
        {
            Entry entry;
            bool wasDisplayed;
            bool nowDisplayed;
            lock (_sync)
            {
                if (!_entries.TryGetValue(id, out entry))
                {
                    HtmlUiLogger.Warn("Surface update ignored: not registered: " + id);
                    return false;
                }
                if (entry.Surface.Enabled == enabled) return true;
                wasDisplayed = IsDisplayed(entry);
                entry.Surface.Enabled = enabled;
                entry.Sequence = NextSequenceLocked();
                nowDisplayed = IsDisplayed(entry);
            }

            if (!wasDisplayed && nowDisplayed) SafeInvokeOpened(entry.Surface, id);
            else if (wasDisplayed && !nowDisplayed) SafeInvokeClosed(entry.Surface, id);

            Publish();
            NotifyChanged();
            return true;
        }

        public bool IsVisible(string id)
        {
            Entry entry;
            lock (_sync)
            {
                if (!_entries.TryGetValue(id, out entry)) return false;
                return IsDisplayed(entry);
            }
        }

        public System.Collections.Generic.IEnumerable<HtmlUiSurfaceView> All
        {
            get
            {
                List<Entry> snapshot;
                lock (_sync) snapshot = new List<Entry>(_entries.Values);
                var result = new System.Collections.Generic.List<HtmlUiSurfaceView>();
                foreach (var entry in snapshot) result.Add(BuildView(entry));
                return result;
            }
        }

        public HtmlUiSurfaceAggregate Aggregate
        {
            get { lock (_sync) return ComputeAggregateLocked(); }
        }

        /// <summary>
        /// Framework-level suppression used by the Page-dominant policy. Suppression does not
        /// change consumer intent, so surfaces restore automatically when the page closes.
        /// </summary>
        internal void SetSuppressedByPage(bool suppressed)
        {
            List<Entry> flipped = null;
            lock (_sync)
            {
                if (_suppressAll == suppressed) return;
                _suppressAll = suppressed;
                foreach (var pair in _entries)
                {
                    var entry = pair.Value;
                    var wasDisplayed = IsDisplayed(entry);
                    entry.Suppressed = suppressed;
                    if (wasDisplayed != IsDisplayed(entry))
                    {
                        flipped = flipped ?? new List<Entry>();
                        flipped.Add(entry);
                    }
                }
            }
            HtmlUiLogger.Info("Surface suppression by page: " + suppressed);
            if (flipped != null)
            {
                foreach (var entry in flipped)
                {
                    if (suppressed) SafeInvokeClosed(entry.Surface, entry.Surface.Id);
                    else SafeInvokeOpened(entry.Surface, entry.Surface.Id);
                }
            }
            Publish();
            NotifyChanged();
        }

        private bool Mutate(string id, Func<Entry, bool> mutate)
        {
            Entry entry;
            bool changed;
            lock (_sync)
            {
                if (!_entries.TryGetValue(id, out entry))
                {
                    HtmlUiLogger.Warn("Surface update ignored: not registered: " + id);
                    return false;
                }
                changed = mutate(entry);
            }
            if (!changed) return true;
            Publish();
            NotifyChanged();
            return true;
        }

        private static bool IsDisplayed(Entry entry)
        {
            return entry.Visible && !entry.Suppressed && entry.Surface.Enabled;
        }

        private long NextSequenceLocked() => ++_sequence;

        private HtmlUiSurfaceAggregate ComputeAggregateLocked()
        {
            var result = new HtmlUiSurfaceAggregate { EffectiveInputMode = HtmlUiInputMode.Hidden, InputOwnerId = null, HasVisible = false };

            Entry best = null;
            int bestStrength = -1;

            foreach (var pair in _entries)
            {
                var entry = pair.Value;
                if (!IsDisplayed(entry)) continue;

                result.HasVisible = true;
                var strength = StrengthOf(entry.Surface.InputDemand);
                if (strength <= 0) continue;

                if (strength > bestStrength || (strength == bestStrength && WinsTie(entry, best)))
                {
                    bestStrength = strength;
                    best = entry;
                }
            }

            if (!result.HasVisible)
            {
                result.EffectiveInputMode = HtmlUiInputMode.Hidden;
                return result;
            }

            result.EffectiveInputMode = best == null ? HtmlUiInputMode.Passive : best.Surface.InputDemand;
            result.InputOwnerId = best == null ? null : best.Surface.Id;
            return result;
        }

        private static bool WinsTie(Entry candidate, Entry current)
        {
            if (current == null) return true;
            if (candidate.Surface.ZIndex != current.Surface.ZIndex) return candidate.Surface.ZIndex > current.Surface.ZIndex;
            return candidate.Sequence >= current.Sequence;
        }

        private static int StrengthOf(HtmlUiInputMode mode)
        {
            switch (mode)
            {
                case HtmlUiInputMode.Hidden: return 0;
                case HtmlUiInputMode.Passive: return 1;
                case HtmlUiInputMode.MouseCaptured: return 2;
                case HtmlUiInputMode.Captured: return 3;
                default: return 0;
            }
        }

        private HtmlUiSurfaceView BuildView(Entry entry)
        {
            string uri = null;
            try { uri = _host == null ? null : _host.BuildSurfaceUri(entry.Surface); }
            catch (Exception ex) { HtmlUiLogger.Error("Failed to build surface uri: " + entry.Surface.Id, ex); }

            return new HtmlUiSurfaceView
            {
                Id = entry.Surface.Id,
                OwnerId = entry.Surface.OwnerId,
                ContentRootId = entry.Surface.ContentRootId,
                RelativePath = entry.Surface.RelativePath,
                Uri = uri,
                ZIndex = entry.Surface.ZIndex,
                Visible = IsDisplayed(entry),
                Enabled = entry.Surface.Enabled,
                Suppressed = entry.Suppressed,
                InputDemand = entry.Surface.InputDemand
            };
        }

        private void Publish()
        {
            if (_host == null) return;
            try
            {
                List<Entry> snapshot;
                HtmlUiSurfaceAggregate aggregate;
                lock (_sync)
                {
                    snapshot = new List<Entry>(_entries.Values);
                    aggregate = ComputeAggregateLocked();
                }

                // BuildSurfaceUri touches host content-root state, so it must stay out of _sync.
                var views = new System.Collections.Generic.List<HtmlUiSurfaceView>();
                foreach (var entry in snapshot) views.Add(BuildView(entry));

                // Stable render order for the shell: z-index first, registration order as tie-break.
                views.Sort((a, b) => a.ZIndex == b.ZIndex ? string.Compare(a.Id, b.Id, StringComparison.OrdinalIgnoreCase) : a.ZIndex.CompareTo(b.ZIndex));

                // The wire format is lowerCamelCase, matching every other framework state payload.
                var wire = new List<object>();
                foreach (var view in views)
                {
                    wire.Add(new
                    {
                        id = view.Id,
                        ownerId = view.OwnerId,
                        contentRootId = view.ContentRootId,
                        relativePath = view.RelativePath,
                        uri = view.Uri,
                        zIndex = view.ZIndex,
                        visible = view.Visible,
                        enabled = view.Enabled,
                        suppressed = view.Suppressed,
                        inputDemand = view.InputDemand.ToString()
                    });
                }

                _host.State.Set(StateKey, wire);
                _host.State.Set(AggregateStateKey, new
                {
                    effectiveInputMode = aggregate.EffectiveInputMode.ToString(),
                    inputOwnerId = aggregate.InputOwnerId ?? string.Empty,
                    hasVisible = aggregate.HasVisible
                });
            }
            catch (Exception ex)
            {
                HtmlUiLogger.Error("Failed to publish surface state.", ex);
            }
        }

        private void NotifyChanged()
        {
            try { SurfacesChanged?.Invoke(); }
            catch (Exception ex) { HtmlUiLogger.Error("Surface change notification failed.", ex); }
        }

        private static void SafeInvokeOpened(HtmlUiSurface surface, string id)
        {
            try { surface.Opened?.Invoke(); }
            catch (Exception ex) { HtmlUiLogger.Error("Surface open callback failed: " + id, ex); }
        }

        private static void SafeInvokeClosed(HtmlUiSurface surface, string id)
        {
            try { surface.Closed?.Invoke(); }
            catch (Exception ex) { HtmlUiLogger.Error("Surface close callback failed: " + id, ex); }
        }
    }
}
