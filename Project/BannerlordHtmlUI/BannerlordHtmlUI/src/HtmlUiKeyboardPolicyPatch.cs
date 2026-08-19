using System;
using System.Diagnostics;
using System.Reflection;
using System.Windows.Forms;
using HarmonyLib;

namespace BannerlordHtmlUI
{
    /// <summary>
    /// Applies the page-level keyboard policy without changing the Framework's public input modes.
    /// A Passthrough page still captures mouse input, but it does not activate the WebView window.
    /// </summary>
    internal static class HtmlUiKeyboardPolicyPatch
    {
        private const string HarmonyId = "BannerlordHtmlUI.KeyboardPolicy";
        private static readonly object Sync = new object();
        private static Harmony _harmony;
        private static bool _installed;
        private static FieldInfo _formField;

        public static void Install()
        {
            lock (Sync)
            {
                if (_installed) return;

                _formField = typeof(HtmlUiHost).GetField("_form", BindingFlags.Instance | BindingFlags.NonPublic);
                var setInputMode = AccessTools.Method(typeof(HtmlUiHost), nameof(HtmlUiHost.SetInputMode));
                if (_formField == null || setInputMode == null)
                    throw new MissingMemberException("HtmlUiHost input policy members were not found.");

                _harmony = new Harmony(HarmonyId);
                _harmony.Patch(
                    setInputMode,
                    postfix: new HarmonyMethod(
                        typeof(HtmlUiKeyboardPolicyPatch),
                        nameof(AfterSetInputMode)));

                _installed = true;
                HtmlUiLogger.Info("Page keyboard policy patch installed.");
            }
        }

        public static void Uninstall()
        {
            lock (Sync)
            {
                if (!_installed) return;
                try { _harmony?.UnpatchAll(HarmonyId); }
                finally
                {
                    _harmony = null;
                    _formField = null;
                    _installed = false;
                }
            }
        }

        private static void AfterSetInputMode(HtmlUiHost __instance, HtmlUiInputMode mode)
        {
            try
            {
                if (__instance == null || mode != HtmlUiInputMode.Captured) return;
                var page = __instance.Pages.Current;
                if (page == null || page.KeyboardInputMode != HtmlUiKeyboardInputMode.Passthrough) return;

                var form = _formField?.GetValue(__instance) as Form;
                if (form == null || form.IsDisposed || !form.IsHandleCreated) return;

                var gameWindow = Process.GetCurrentProcess().MainWindowHandle;
                form.BeginInvoke(new Action(() =>
                {
                    if (form.IsDisposed || !form.IsHandleCreated) return;

                    // Keep the overlay hit-test active, but make it non-activating so Bannerlord keeps keyboard focus.
                    Win32.SetNoActivate(form.Handle, true);
                    Win32.ShowWindow(form.Handle, Win32.SW_SHOWNOACTIVATE);

                    if (gameWindow != IntPtr.Zero && Win32.IsWindow(gameWindow))
                        Win32.SetForegroundWindow(gameWindow);

                    HtmlUiLogger.Info("Keyboard policy: Passthrough applied to page " + page.Id + "; mouse capture retained, game keyboard focus restored.");
                }));
            }
            catch (Exception ex)
            {
                HtmlUiLogger.Warn("Keyboard passthrough policy application failed: " + ex.GetBaseException().Message);
            }
        }
    }
}
