using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using TaleWorlds.InputSystem;

namespace BannerlordHtmlUI
{
    public static class HtmlUiInputTraceLogger
    {
        private static readonly object Sync = new object();
        private static readonly Dictionary<InputKey, bool> LastDown = new Dictionary<InputKey, bool>();
        private static readonly List<InputKey> TracedKeys = new List<InputKey>();
        private static string _path;
        private static bool _initialized;
        private static IntPtr _lastForeground;
        private static HtmlUiInputMode _lastMode = HtmlUiInputMode.Hidden;
        private static string _lastPage = null;
        private static long _lastTickStart;
        private static long _lastTickAfterBase;
        private static long _lastTickAfterInput;
        private static long _lastTickAfterService;
        private static long _lastTickAfterF10;
        private static long _lastTickCompleted;
        private static long _tickCount;

        public static void Initialize(string moduleDirectory)
        {
            if (string.IsNullOrWhiteSpace(moduleDirectory)) return;
            try
            {
                Directory.CreateDirectory(moduleDirectory);
                _path = Path.Combine(moduleDirectory, "BannerlordHtmlUI_InputTrace.log");
                lock (Sync) File.WriteAllText(_path, string.Empty);
                LastDown.Clear();
                TracedKeys.Clear();
                BuildTracedKeySet();
                _tickCount = 0;
                _lastTickStart = 0;
                _lastTickAfterBase = 0;
                _lastTickAfterInput = 0;
                _lastTickAfterService = 0;
                _lastTickAfterF10 = 0;
                _lastTickCompleted = 0;
                _initialized = true;
                Write("=== INPUT TRACE STARTED === tracedKeys=" + TracedKeys.Count);
            }
            catch { _initialized = false; }
        }

        private static void BuildTracedKeySet()
        {
            var keys = new[]
            {
                InputKey.M, InputKey.N,
                InputKey.LeftShift, InputKey.RightShift,
                InputKey.LeftControl, InputKey.RightControl,
                InputKey.LeftAlt, InputKey.RightAlt, InputKey.Tab,
                InputKey.Escape,
                InputKey.LeftMouseButton, InputKey.RightMouseButton,
                InputKey.MiddleMouseButton, InputKey.MouseScrollUp, InputKey.MouseScrollDown
            };
            foreach (var key in keys) TracedKeys.Add(key);
        }

        public static void Shutdown()
        {
            if (!_initialized) return;
            Write("=== INPUT TRACE STOPPED ===");
            _initialized = false;
        }

        public static void Event(string message) => Write("EVENT " + message);

        public static void TickStart(float dt)
        {
            if (!_initialized) return;
            _tickCount++;
            _lastTickStart = StopwatchTicks();
        }

        public static void TickAfterBase()
        {
            if (!_initialized) return;
            _lastTickAfterBase = StopwatchTicks();
        }

        public static void TickAfterInput()
        {
            if (!_initialized) return;
            _lastTickAfterInput = StopwatchTicks();
        }

        public static void TickAfterService()
        {
            if (!_initialized) return;
            _lastTickAfterService = StopwatchTicks();
        }

        public static void TickAfterF10()
        {
            if (!_initialized) return;
            _lastTickAfterF10 = StopwatchTicks();
        }

        public static void TickCompleted()
        {
            if (!_initialized) return;
            _lastTickCompleted = StopwatchTicks();
        }

        public static long TickCount => Interlocked.Read(ref _tickCount);
        public static long LastTickStartTimestamp => Interlocked.Read(ref _lastTickStart);
        public static long LastTickAfterBaseTimestamp => Interlocked.Read(ref _lastTickAfterBase);
        public static long LastTickAfterInputTimestamp => Interlocked.Read(ref _lastTickAfterInput);
        public static long LastTickAfterServiceTimestamp => Interlocked.Read(ref _lastTickAfterService);
        public static long LastTickAfterF10Timestamp => Interlocked.Read(ref _lastTickAfterF10);
        public static long LastTickCompletedTimestamp => Interlocked.Read(ref _lastTickCompleted);

        public static void KeyMessage(int msg, long wParam, long lParam, string source)
        {
            Write("WINMSG source=" + source + " msg=0x" + msg.ToString("X4") + " vk=0x" + wParam.ToString("X2") + " lParam=0x" + lParam.ToString("X8"));
        }

        public static void MouseMessage(int msg, long wParam, long lParam, string source)
        {
            var x = unchecked((short)(lParam & 0xFFFF));
            var y = unchecked((short)((lParam >> 16) & 0xFFFF));
            Write("MOUSE source=" + source + " msg=0x" + msg.ToString("X4") + " x=" + x + " y=" + y + " wParam=0x" + wParam.ToString("X"));
        }

        public static void BannerlordInput(HtmlUiHost host)
        {
            if (!_initialized) return;
            try
            {
                foreach (var key in TracedKeys)
                {
                    bool down;
                    try { down = Input.IsKeyDown(key); }
                    catch { continue; }

                    bool previous;
                    if (!LastDown.TryGetValue(key, out previous))
                    {
                        LastDown[key] = down;
                        if (!down) continue;
                        previous = false;
                    }

                    if (down == previous) continue;
                    LastDown[key] = down;
                    Write("GAME_INPUT " + (down ? "DOWN" : "UP") + " key=" + key + " type=" + GetInputTypeSafe(key));
                }

                var foreground = Win32.GetForegroundWindow();
                var mode = host == null ? HtmlUiInputMode.Hidden : host.InputMode;
                var page = host?.Pages?.CurrentId;
                if (foreground != _lastForeground || mode != _lastMode || !string.Equals(page, _lastPage, StringComparison.Ordinal))
                {
                    _lastForeground = foreground;
                    _lastMode = mode;
                    _lastPage = page;
                    var game = IntPtr.Zero;
                    var overlay = IntPtr.Zero;
                    try
                    {
                        var field = host?.GetType().GetField("_form", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                        var value = field?.GetValue(host) as HtmlUiOverlayForm;
                        overlay = value != null && value.IsHandleCreated ? value.Handle : IntPtr.Zero;
                        Win32.TryGetGameWindowHandle(overlay, out game);
                    }
                    catch { }
                    Write("STATE foreground=" + foreground + " game=" + game + " overlay=" + overlay + " inputMode=" + mode + " page=" + (page ?? "<null>"));
                }
            }
            catch (Exception ex)
            {
                Write("TRACE_ERROR BannerlordInput " + ex.GetBaseException().Message);
            }
        }

        private static string GetInputTypeSafe(InputKey key)
        {
            try { return Key.GetInputType(key).ToString(); }
            catch { return "Unknown"; }
        }

        private static long StopwatchTicks() => System.Diagnostics.Stopwatch.GetTimestamp();

        private static void Write(string message)
        {
            if (!_initialized || string.IsNullOrWhiteSpace(_path)) return;
            var line = "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "] [" + Thread.CurrentThread.ManagedThreadId + "] " + message;
            try { lock (Sync) File.AppendAllText(_path, line + Environment.NewLine); }
            catch { }
        }
    }
}
