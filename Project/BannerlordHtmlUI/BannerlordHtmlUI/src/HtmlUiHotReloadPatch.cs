using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace BannerlordHtmlUI
{
    internal static class HtmlUiHotReloadPatch
    {
        private const long DebounceMilliseconds = 75L;

        private sealed class ReloadState
        {
            public long LastReloadTick;
        }

        private static readonly object Sync = new object();
        private static readonly ConditionalWeakTable<HtmlUiHost, ReloadState> States =
            new ConditionalWeakTable<HtmlUiHost, ReloadState>();
        private static bool _installed;
        private static Harmony _harmony;

        public static void Install(HtmlUiHost host)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));

            lock (Sync)
            {
                if (_installed) return;

                _harmony = new Harmony("BannerlordHtmlUI.HotReload");
                var method = AccessTools.Method(typeof(HtmlUiHost), "Reload");
                if (method == null)
                    throw new MissingMethodException("HtmlUiHost.Reload was not found.");

                _harmony.Patch(
                    method,
                    prefix: new HarmonyMethod(typeof(HtmlUiHotReloadPatch), nameof(BeforeReload)));

                _installed = true;
                HtmlUiLogger.Info("Hot reload lifecycle/debounce patch installed.");
            }
        }

        private static bool BeforeReload(HtmlUiHost __instance)
        {
            if (__instance == null || !__instance.HotReloadEnabled)
                return true;

            // A watcher can survive Page.CloseCurrent(). Do not reload a hidden/no-current-page host.
            if (!__instance.IsVisible || __instance.Pages == null || __instance.Pages.Current == null)
            {
                HtmlUiLogger.Debug("Hot reload ignored: host has no active visible page.");
                return false;
            }

            var state = States.GetOrCreateValue(__instance);
            var now = GetMonotonicMilliseconds();
            lock (state)
            {
                if (now - state.LastReloadTick < DebounceMilliseconds)
                {
                    HtmlUiLogger.Debug("Hot reload debounced.");
                    return false;
                }

                state.LastReloadTick = now;
            }

            return true;
        }

        private static long GetMonotonicMilliseconds()
        {
            return Stopwatch.GetTimestamp() * 1000L / Stopwatch.Frequency;
        }
    }
}