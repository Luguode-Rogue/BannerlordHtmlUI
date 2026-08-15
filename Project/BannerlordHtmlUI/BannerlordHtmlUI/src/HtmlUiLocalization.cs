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
        private static readonly HashSet<string> MissingWarningKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static string _lastLanguage;

        // This is the user-facing language title returned by Bannerlord's active-language property.
        // Keep it for diagnostics/UI state. LocalizationManager.GetTranslatedText requires the language ID.
        public static string CurrentLanguage
        {
            get
            {
                try { return MBTextManager.ActiveTextLanguage ?? LocalizedTextManager.DefaultEnglishLanguageId; }
                catch { return LocalizedTextManager.DefaultEnglishLanguageId; }
            }
        }

        // Resolve Bannerlord's active language to the language ID expected by LocalizedTextManager.
        // ActiveTextLanguage can be a display title such as "简体中文", while GetTranslatedText expects
        // an ID such as "Chinese". Match both the ID and title so this remains compatible across versions.
        private static string CurrentLanguageId
        {
            get
            {
                var active = CurrentLanguage;
                if (string.IsNullOrWhiteSpace(active))
                    return LocalizedTextManager.DefaultEnglishLanguageId;

                try
                {
                    var ids = LocalizedTextManager.GetLanguageIds(false);
                    if (ids != null)
                    {
                        foreach (var id in ids)
                        {
                            if (string.IsNullOrWhiteSpace(id)) continue;
                            if (string.Equals(id, active, StringComparison.OrdinalIgnoreCase))
                                return id;

                            string title = null;
                            try { title = LocalizedTextManager.GetLanguageTitle(id); }
                            catch { }
                            if (!string.IsNullOrWhiteSpace(title) &&
                                string.Equals(title, active, StringComparison.OrdinalIgnoreCase))
                                return id;
                        }
                    }
                }
                catch (Exception ex)
                {
                    HtmlUiLogger.Debug("Failed to resolve active localization language ID: " + ex.GetBaseException().Message);
                }

                return active;
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

            var activeLanguage = CurrentLanguage;
            var activeLanguageId = CurrentLanguageId;
            var text = Lookup(activeLanguageId, key);
            var resolvedLanguage = activeLanguageId;
            var found = !IsMissing(text);

            if (!found && !string.IsNullOrWhiteSpace(fallbackLanguage) &&
                !string.Equals(fallbackLanguage, activeLanguageId, StringComparison.OrdinalIgnoreCase))
            {
                var fallbackId = ResolveLanguageId(fallbackLanguage);
                text = Lookup(fallbackId, key);
                resolvedLanguage = fallbackId;
                found = !IsMissing(text);
            }

            if (!found && !string.Equals(activeLanguageId, LocalizedTextManager.DefaultEnglishLanguageId, StringComparison.OrdinalIgnoreCase))
            {
                text = Lookup(LocalizedTextManager.DefaultEnglishLanguageId, key);
                resolvedLanguage = LocalizedTextManager.DefaultEnglishLanguageId;
                found = !IsMissing(text);
            }

            if (!found) text = key;
            text = ApplyVariables(text, variables);

            if (!found)
                WarnMissingOnce(activeLanguage, key);

            return new
            {
                key,
                text,
                found,
                language = resolvedLanguage,
                requestedLanguage = activeLanguage
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
                // The warning cache is language-specific. Reset it on every
                // language transition so diagnostics for the new locale are
                // accurate and the set cannot grow indefinitely as languages change.
                MissingWarningKeys.Clear();
                return true;
            }
        }

        public static void InitializeState()
        {
            lock (Sync)
            {
                _lastLanguage = CurrentLanguage;
                MissingWarningKeys.Clear();
            }
        }

        public static string FormatDate(DateTime value)
        {
            return LocalizedTextManager.GetDateFormattedByLanguage(CurrentLanguageId, value);
        }

        public static string FormatTime(DateTime value)
        {
            return LocalizedTextManager.GetTimeFormattedByLanguage(CurrentLanguageId, value);
        }

        private static string ResolveLanguageId(string language)
        {
            if (string.IsNullOrWhiteSpace(language))
                return LocalizedTextManager.DefaultEnglishLanguageId;

            try
            {
                var ids = LocalizedTextManager.GetLanguageIds(false);
                if (ids != null)
                {
                    foreach (var id in ids)
                    {
                        if (string.IsNullOrWhiteSpace(id)) continue;
                        if (string.Equals(id, language, StringComparison.OrdinalIgnoreCase))
                            return id;

                        string title = null;
                        try { title = LocalizedTextManager.GetLanguageTitle(id); }
                        catch { }
                        if (!string.IsNullOrWhiteSpace(title) &&
                            string.Equals(title, language, StringComparison.OrdinalIgnoreCase))
                            return id;
                    }
                }
            }
            catch (Exception ex)
            {
                HtmlUiLogger.Debug("Failed to resolve localization language ID: " + language + " | " + ex.GetBaseException().Message);
            }

            return language;
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

        private static void WarnMissingOnce(string language, string key)
        {
            var warningKey = (language ?? "") + "\n" + key;
            lock (Sync)
            {
                if (!MissingWarningKeys.Add(warningKey)) return;
            }

            HtmlUiLogger.Warn("Localization key not found: " + key + " (language=" + language + ")");
        }

        private static void ClearMissingWarningsForLanguage(string language)
        {
            var prefix = (language ?? "") + "\n";
            var stale = new List<string>();
            foreach (var warningKey in MissingWarningKeys)
            {
                if (warningKey.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    stale.Add(warningKey);
            }

            foreach (var warningKey in stale)
                MissingWarningKeys.Remove(warningKey);
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