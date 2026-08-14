using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace BannerlordHtmlUI
{
    public sealed class HtmlUiStateStore
    {
        private readonly Dictionary<string, object> _values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        private readonly HtmlUiHost _host;
        private readonly object _sync = new object();

        internal HtmlUiStateStore(HtmlUiHost host) => _host = host;

        public void Set(string key, object value)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("State key is required.", nameof(key));

            bool changed;
            lock (_sync)
            {
                if (_values.TryGetValue(key, out var existing) && Equals(existing, value))
                    return;

                _values[key] = value;
                changed = true;
            }

            if (changed)
                _host.SendEvent("state:" + key, value);
        }

        public bool TryGet(string key, out object value)
        {
            lock (_sync) return _values.TryGetValue(key, out value);
        }

        public void Remove(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("State key is required.", nameof(key));

            bool removed;
            lock (_sync) removed = _values.Remove(key);
            if (!removed) return;

            // Runtime state subscriptions are keyed by `state:<key>`.
            // Publish null on the same channel so subscribers and binders see the removal.
            _host.SendEvent("state:" + key, null);
        }

        public IReadOnlyDictionary<string, object> GetSnapshot()
        {
            lock (_sync) return new Dictionary<string, object>(_values, StringComparer.OrdinalIgnoreCase);
        }

        public string SnapshotJson()
        {
            lock (_sync) return JsonConvert.SerializeObject(_values);
        }
    }
}
