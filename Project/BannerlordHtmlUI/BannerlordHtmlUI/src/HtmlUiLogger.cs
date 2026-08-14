using System;
using System.IO;
using System.Threading;

namespace BannerlordHtmlUI
{
    public static class HtmlUiLogger
    {
        private static readonly object Sync = new object();
        private static string _logPath;
        public static bool Enabled { get; set; } = true;

        // Debug is intentionally disabled by default. It is for temporary diagnostics only
        // and must not turn normal bridge/lifecycle traffic into a high-volume log stream.
        public static bool DebugEnabled { get; set; } = false;

        public static void Initialize(string moduleDirectory)
        {
            if (string.IsNullOrWhiteSpace(moduleDirectory)) return;
            Directory.CreateDirectory(moduleDirectory);
            _logPath = Path.Combine(moduleDirectory, "BannerlordHtmlUI.log");
            Write("=== BannerlordHtmlUI started ===");
        }

        public static void Debug(string message)
        {
            if (!DebugEnabled) return;
            Write("DEBUG " + message);
        }

        public static void Info(string message) => Write("INFO  " + message);
        public static void Warn(string message) => Write("WARN  " + message);
        public static void Error(string message, Exception ex = null) => Write("ERROR " + message + (ex == null ? "" : " | " + ex));

        private static void Write(string message)
        {
            if (!Enabled) return;
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{Thread.CurrentThread.ManagedThreadId}] {message}";
            try
            {
                lock (Sync)
                {
                    if (!string.IsNullOrWhiteSpace(_logPath)) File.AppendAllText(_logPath, line + Environment.NewLine);
                }
            }
            catch { }
            try { TaleWorlds.Library.Debug.Print("[BannerlordHtmlUI] " + line); } catch { }
        }
    }
}
