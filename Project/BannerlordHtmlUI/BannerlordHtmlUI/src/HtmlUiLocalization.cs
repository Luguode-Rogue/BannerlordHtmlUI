using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;
using TaleWorlds.Localization;

namespace BannerlordHtmlUI
{
    public static class HtmlUiLocalization
    {
        private static readonly object Sync = new object();
        private static string _lastLanguage;

        public static string CurrentLanguage
        {
            get
            {
                try { return MBTextManager.ActiveTextLanguage ?? LocalizedTextManager.DefaultEnglishLanguageId; }
                catch { return LocalizedTextManager.DefaultEnglishLanguageId; }
            }
        }

        public static IReadOnlyList<object> GetLanguages()
        {
            var result = new List<object>();
            List<string> ids;
            try { ids = LocalizedTextManager.GetLanguageIds(false) ?? new List<string>(); }
            catch { ids = new List<string>(); }

            foreach (var id in ids)
            {
                if (string.IsNullOrWhiteSpace(id)) continue;
                string title;
                try { title = LocalizedTextManager.GetLanguageTitle(id) ?? id; }
                catch { title = id; }
                result.Add(new { id, title });
            }
            return result;
        }

        public static object Translate(string key, JObject variables = null, string fallbackLanguage = null)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Localization key is required.", nameof(key));

            var active = CurrentLanguage;
            var text = Lookup(active, key);
            var resolvedLanguage = active;
            var found = !IsMissing(text);

            if (!found && !string.IsNullOrWhiteSpace(fallbackLanguage) && !string.Equals(fallbackLanguage, active, StringComparison.OrdinalIgnoreCase))
            {
                text = Lookup(fallbackLanguage, key);
                resolvedLanguage = fallbackLanguage;
                found = !IsMissing(text);
            }

            if (!found && !string.Equals(active, LocalizedTextManager.DefaultEnglishLanguageId, StringComparison.OrdinalIgnoreCase))
            {
                text = Lookup(LocalizedTextManager.DefaultEnglishLanguageId, key);
                resolvedLanguage = LocalizedTextManager.DefaultEnglishLanguageId;
                found = !IsMissing(text);
            }

            if (!found) text = key;
            text = ApplyVariables(text, variables);

            if (!found)
                HtmlUiLogger.Warn("Localization key not found: " + key + " (language=" + active + ")");

            return new
            {
                key,
                text,
                found,
                language = resolvedLanguage,
                requestedLanguage = active
            };
        }

        public static object TranslateMany(JObject payload)
        {
            var keys = payload?["keys"] as JArray ?? new JArray();
            var variables = payload?["variables"] as JObject;
            var fallbackLanguage = payload?["fallbackLanguage"]?.Value<string>();
            var result = new JObject();

            foreach (var token in keys)
            {
                var key = token?.Value<string>();
                if (string.IsNullOrWhiteSpace(key)) continue;
                var keyVariables = variables?[key] as JObject;
                var translated = (JObject)JToken.FromObject(Translate(key, keyVariables, fallbackLanguage));
                result[key] = translated;
            }

            return new
            {
                language = CurrentLanguage,
                values = result
            };
        }

        public static bool TryPublishLanguageChange(out string language)
        {
            language = CurrentLanguage;
            lock (Sync)
            {
                if (string.Equals(_lastLanguage, language, StringComparison.OrdinalIgnoreCase)) return false;
                _lastLanguage = language;
                return true;
            }
        }

        public static void InitializeState()
        {
            lock (Sync) _lastLanguage = CurrentLanguage;
        }

        public static string FormatDate(DateTime value)
        {
            return LocalizedTextManager.GetDateFormattedByLanguage(CurrentLanguage, value);
        }

        public static string FormatTime(DateTime value)
        {
            return LocalizedTextManager.GetTimeFormattedByLanguage(CurrentLanguage, value);
        }

        private static string Lookup(string language, string key)
        {
            try { return LocalizedTextManager.GetTranslatedText(language, key); }
            catch (Exception ex)
            {
                HtmlUiLogger.Warn("Localization lookup failed: " + key + " | " + ex.Message);
                return null;
            }
        }

        private static bool IsMissing(string text)
        {
            return string.IsNullOrWhiteSpace(text) || string.Equals(text, "<MISSING>", StringComparison.OrdinalIgnoreCase);
        }

        private static string ApplyVariables(string text, JObject variables)
        {
            if (string.IsNullOrEmpty(text) || variables == null) return text;
            foreach (var property in variables.Properties())
            {
                var value = ToVariableString(property.Value);
                text = text.Replace("{" + property.Name + "}", value);
            }
            return text;
        }

        private static string ToVariableString(JToken value)
        {
            if (value == null || value.Type == JTokenType.Null) return string.Empty;
            if (value is JValue primitive)
            {
                if (primitive.Type == JTokenType.Date)
                {
                    return Convert.ToDateTime(primitive.Value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
                }

                return Convert.ToString(primitive.Value, CultureInfo.InvariantCulture) ?? string.Empty;
            }

            // Objects/arrays are valid JSON inputs too; serialize them instead of
            // throwing an InvalidCastException from a hard JValue cast.
            return value.ToString(Newtonsoft.Json.Formatting.None);
        }
    }
}
