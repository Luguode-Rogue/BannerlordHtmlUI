using System;
using System.Collections.Concurrent;
using HarmonyLib;
using Microsoft.Web.WebView2.Core;

namespace BannerlordHtmlUI
{
    /// <summary>
    /// Prevents an older WebView2 NavigationCompleted callback from being interpreted
    /// as completion of the newer current page navigation.
    ///
    /// HtmlUiHost intentionally keeps its existing navigation implementation unchanged;
    /// this patch only adds a NavigationId guard around the existing callbacks.
    /// </summary>
    internal static class HtmlUiNavigationRacePatch
    {
        private static readonly object Sync = new object();
        private static readonly ConcurrentDictionary<HtmlUiHost, long> CurrentNavigationIds =
            new ConcurrentDictionary<HtmlUiHost, long>();

        private static bool _installed;
        private static Harmony _harmony;

        public static void Install(HtmlUiHost host)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));

            lock (Sync)
            {
                if (_installed)
                    return;

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
            if (__instance == null || e == null || e.Cancel)
                return;

            CurrentNavigationIds[__instance] = e.NavigationId;
        }

        private static bool OnNavigationCompletedPrefix(
            HtmlUiHost __instance,
            CoreWebView2NavigationCompletedEventArgs e)
        {
            if (__instance == null || e == null)
                return true;

            if (!CurrentNavigationIds.TryGetValue(__instance, out var currentId))
                return true;

            if (currentId != e.NavigationId)
            {
                // A newer navigation is already in flight. Suppress the old completion
                // so HtmlUiHost cannot publish the newer Pages.Current as ready early.
                return false;
            }

            CurrentNavigationIds.TryRemove(__instance, out _);
            return true;
        }
    }
}
