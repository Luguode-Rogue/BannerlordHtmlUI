using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Reflection;
using HarmonyLib;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

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
        private static readonly ConditionalWeakTable<HtmlUiHost, Task<string>> RuntimeRegistrationBarriers =
            new ConditionalWeakTable<HtmlUiHost, Task<string>>();

        private static bool _installed;
        private static Harmony _harmony;
        private static MethodInfo _navigateOnUiThread;

        public static void Install(HtmlUiHost host)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));

            lock (Sync)
            {
                if (!_installed)
                {
                    _harmony = new Harmony("BannerlordHtmlUI.NavigationRace");

                    var starting = AccessTools.Method(typeof(HtmlUiHost), "OnNavigationStarting");
                    var completed = AccessTools.Method(typeof(HtmlUiHost), "OnNavigationCompleted");
                    _navigateOnUiThread = AccessTools.Method(typeof(HtmlUiHost), "NavigateOnUiThread");
                    if (starting == null || completed == null || _navigateOnUiThread == null)
                        throw new MissingMethodException("HtmlUiHost navigation handlers were not found.");

                    _harmony.Patch(
                        starting,
                        postfix: new HarmonyMethod(typeof(HtmlUiNavigationRacePatch), nameof(OnNavigationStartingPostfix)));

                    _harmony.Patch(
                        completed,
                        prefix: new HarmonyMethod(typeof(HtmlUiNavigationRacePatch), nameof(OnNavigationCompletedPrefix)));

                    _harmony.Patch(
                        _navigateOnUiThread,
                        prefix: new HarmonyMethod(typeof(HtmlUiNavigationRacePatch), nameof(OnNavigateOnUiThreadPrefix)));

                    _installed = true;
                    HtmlUiLogger.Info("Navigation race guard installed.");
                }

                // The barrier belongs to the host instance, not to the static Harmony patch.
                // This matters if the WebView2 host is destroyed and recreated in the same
                // process: a new host must get its own document-created runtime barrier.
                if (!RuntimeRegistrationBarriers.TryGetValue(host, out _))
                    RuntimeRegistrationBarriers.Add(host, CreateRuntimeRegistrationBarrier(host));
            }
        }

        public static void Uninstall(HtmlUiHost host)
        {
            lock (Sync)
            {
                if (host != null)
                    RuntimeRegistrationBarriers.Remove(host);

                if (!_installed || _harmony == null) return;

                try
                {
                    _harmony.Unpatch(
                        AccessTools.Method(typeof(HtmlUiHost), "OnNavigationStarting"),
                        HarmonyPatchType.Postfix,
                        _harmony.Id);
                    _harmony.Unpatch(
                        AccessTools.Method(typeof(HtmlUiHost), "OnNavigationCompleted"),
                        HarmonyPatchType.Prefix,
                        _harmony.Id);
                    _harmony.Unpatch(
                        AccessTools.Method(typeof(HtmlUiHost), "NavigateOnUiThread"),
                        HarmonyPatchType.Prefix,
                        _harmony.Id);
                }
                catch (Exception ex)
                {
                    HtmlUiLogger.Debug("Navigation race guard uninstall failed: " + ex.GetBaseException().Message);
                }
                finally
                {
                    _harmony = null;
                    _navigateOnUiThread = null;
                    _installed = false;
                    HtmlUiLogger.Info("Navigation race guard uninstalled.");
                }
            }
        }

        private static Task<string> CreateRuntimeRegistrationBarrier(HtmlUiHost host)
        {
            try
            {
                var webField = typeof(HtmlUiHost).GetField("_web", BindingFlags.Instance | BindingFlags.NonPublic);
                var web = webField?.GetValue(host) as WebView2;
                var core = web?.CoreWebView2;
                if (core == null)
                {
                    HtmlUiLogger.Warn("Runtime registration barrier could not start: CoreWebView2 is not ready.");
                    return Task.FromResult<string>(null);
                }

                const string script = "window.__bannerlordHtmlUiRuntimeRegistrationBarrier = true;";
                return core.AddScriptToExecuteOnDocumentCreatedAsync(script);
            }
            catch (Exception ex)
            {
                HtmlUiLogger.Error("Failed to arm runtime registration barrier.", ex);
                return Task.FromResult<string>(null);
            }
        }

        private static bool OnNavigateOnUiThreadPrefix(HtmlUiHost __instance, HtmlUiPage page)
        {
            if (__instance == null || page == null) return true;
            if (!RuntimeRegistrationBarriers.TryGetValue(__instance, out var barrier) || barrier == null || barrier.IsCompleted)
                return true;

            _ = ContinueNavigationAfterRuntimeRegistrationAsync(__instance, page, barrier);
            HtmlUiLogger.Info("Navigation deferred until WebView2 runtime registration completed: " + page.Id);
            return false;
        }

        private static async Task ContinueNavigationAfterRuntimeRegistrationAsync(HtmlUiHost host, HtmlUiPage page, Task<string> barrier)
        {
            try
            {
                await barrier;
            }
            catch (Exception ex)
            {
                HtmlUiLogger.Error("Runtime registration barrier failed; continuing navigation: " + page.Id, ex);
            }

            try
            {
                var current = host.Pages.Current;
                if (current == null || !string.Equals(current.Id, page.Id, StringComparison.OrdinalIgnoreCase))
                {
                    HtmlUiLogger.Debug("Suppressed stale deferred page navigation: " + page.Id
                        + ", current=" + (current == null ? "<null>" : current.Id));
                    return;
                }

                var navigate = _navigateOnUiThread;
                if (navigate == null)
                {
                    HtmlUiLogger.Warn("Suppressed deferred navigation because navigation method is unavailable: " + page.Id);
                    return;
                }

                navigate.Invoke(host, new object[] { page });
            }
            catch (Exception ex)
            {
                HtmlUiLogger.Error("Deferred navigation failed: " + page.Id, ex);
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