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
        private long _revision;

        internal HtmlUiStateStore(HtmlUiHost host) => _host = host;

        public int Count
        {
            get
            {
                lock (_sync) return _values.Count;
            }
        }

        public long Revision
        {
            get { lock (_sync) return _revision; }
        }

        public void Set(string key, object value)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("State key is required.", nameof(key));

            long revision;
            lock (_sync)
            {
                if (_values.TryGetValue(key, out var existing) && AreEqual(existing, value))
                    return;

                _values[key] = value;
                revision = ++_revision;
            }

            _host.QueueStateUpdate(key, value, revision, removed: false);
        }

        public bool TryGet(string key, out object value)
        {
            lock (_sync) return _values.TryGetValue(key, out value);
        }

        public void Remove(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("State key is required.", nameof(key));

            bool removed;
            long revision;
            lock (_sync)
            {
                removed = _values.Remove(key);
                revision = removed ? ++_revision : _revision;
            }
            if (!removed) return;

            _host.QueueStateUpdate(key, null, revision, removed: true);
        }

        public IReadOnlyDictionary<string, object> GetSnapshot()
        {
            lock (_sync) return new Dictionary<string, object>(_values, StringComparer.OrdinalIgnoreCase);
        }

        public object GetVersionedSnapshot()
        {
            lock (_sync)
            {
                return new
                {
                    revision = _revision,
                    values = new Dictionary<string, object>(_values, StringComparer.OrdinalIgnoreCase)
                };
            }
        }

        internal string GetVersionedSnapshotJson()
        {
            lock (_sync)
            {
                return JsonConvert.SerializeObject(new
                {
                    revision = _revision,
                    values = new Dictionary<string, object>(_values, StringComparer.OrdinalIgnoreCase)
                });
            }
        }

        public string SnapshotJson()
        {
            lock (_sync) return JsonConvert.SerializeObject(_values);
        }

        private static bool AreEqual(object left, object right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left == null || right == null) return false;

            // Avoid JSON conversion for the common scalar-state case.
            // These are the values most likely to be updated frequently by bindings.
            if (left is string || left is bool || left is char || left is decimal || left is double || left is float ||
                left is byte || left is sbyte || left is short || left is ushort || left is int || left is uint ||
                left is long || left is ulong || left is DateTime || left is DateTimeOffset || left is Guid ||
                left is TimeSpan)
            {
                return left.GetType() == right.GetType() && left.Equals(right);
            }

            try
            {
                return JToken.DeepEquals(JToken.FromObject(left), JToken.FromObject(right));
            }
            catch
            {
                return Equals(left, right);
            }
        }
    }
}
