using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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
            lock (_sync) _values[key] = value;
            _host.SendEvent("state:" + key, value);
        }

        public bool TryGet(string key, out object value)
        {
            lock (_sync) return _values.TryGetValue(key, out value);
        }

        public void Remove(string key)
        {
            lock (_sync) _values.Remove(key);
            _host.SendEvent("state:removed", new { key });
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
