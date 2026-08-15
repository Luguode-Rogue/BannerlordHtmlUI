using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Microsoft.Web.WebView2.Core;

namespace BannerlordHtmlUI
{
    internal static class HtmlUiNavigationRacePatch
    {
        private sealed class NavigationState
        {
            public ulong NavigationId;
        }

        private static readonly object Sync = new object();
        private static readonly ConditionalWeakTable<HtmlUiHost, NavigationState> NavigationStates =
            new ConditionalWeakTable<HtmlUiHost, NavigationState>();

        private static bool _installed;
        private static Harmony _harmony;

        public static void Install(HtmlUiHost host)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));

            lock (Sync)
            {
                if (_installed) return;

                _harmony = new Harmony("BannerlordHtmlUI.NavigationRace");

                var starting = AccessTools.Method(typeof(HtmlUiHost), "OnNavigationStarting");
                var completed = AccessTools.Method(typeof(HtmlUiHost), "OnNavigationCompleted");
                if (starting == null || completed == null)
                    throw new MissingMethodException("HtmlUiHost navigation handlers were not found.");

                _harmony.Patch(
                    starting,
                    postfix: new HarmonyMethod(typeof(HtmlUiNavigationRacePatch), nameof(OnNavigationStartingPostfix)));

                _harmony.Patch(
                    completed,
                    prefix: new HarmonyMethod(typeof(HtmlUiNavigationRacePatch), nameof(OnNavigationCompletedPrefix)));

                _installed = true;
                HtmlUiLogger.Info("Navigation race guard installed.");
            }
        }

        private static void OnNavigationStartingPostfix(
            HtmlUiHost __instance,
            CoreWebView2NavigationStartingEventArgs e)
        {
            if (__instance == null || e == null || e.Cancel) return;
            NavigationStates.GetOrCreateValue(__instance).NavigationId = e.NavigationId;
        }

        private static bool OnNavigationCompletedPrefix(
            HtmlUiHost __instance,
            CoreWebView2NavigationCompletedEventArgs e)
        {
            if (__instance == null || e == null) return true;
            if (!NavigationStates.TryGetValue(__instance, out var state)) return true;

            if (state.NavigationId != e.NavigationId)
            {
                HtmlUiLogger.Debug("Suppressed stale WebView2 navigation completion: " + e.NavigationId);
                return false;
            }

            state.NavigationId = 0;
            return true;
        }
    }
}
