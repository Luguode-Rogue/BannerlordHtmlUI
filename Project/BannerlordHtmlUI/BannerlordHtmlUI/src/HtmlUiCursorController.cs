using System;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.ScreenSystem;

namespace BannerlordHtmlUI
{
    /// <summary>
    /// Makes cursor visibility part of the framework input contract. Bannerlord recomputes mouse
    /// visibility every frame, so a one-shot Win32 ShowCursor call is not sufficient in missions.
    /// </summary>
    internal static class HtmlUiCursorController
    {
        private const string HarmonyId = "BannerlordHtmlUI.CursorController";
        private static readonly object Sync = new object();
        private static Harmony _harmony;
        private static MethodInfo _setMouseVisible;
        private static MethodInfo _updateMouseVisibility;
        private static volatile bool _owned;

        internal static void Install()
        {
            lock (Sync)
            {
                if (_harmony != null) return;
                _setMouseVisible = AccessTools.Method(typeof(ScreenManager), "SetMouseVisible");
                _updateMouseVisibility = AccessTools.Method(typeof(ScreenManager), "UpdateMouseVisibility");
                if (_setMouseVisible == null)
                    throw new MissingMethodException("ScreenManager.SetMouseVisible was not found.");

                _harmony = new Harmony(HarmonyId);
                _harmony.Patch(_setMouseVisible,
                    prefix: new HarmonyMethod(typeof(HtmlUiCursorController), nameof(SetMouseVisiblePrefix)));
                HtmlUiLogger.Info("HTML UI cursor ownership controller installed.");
            }
        }

        internal static void Uninstall()
        {
            lock (Sync)
            {
                _owned = false;
                try { _harmony?.UnpatchAll(HarmonyId); } catch { }
                _harmony = null;
                _setMouseVisible = null;
                _updateMouseVisibility = null;
            }
        }

        internal static void SetOwned(HtmlUiHost host, bool owned)
        {
            if (_owned == owned) return;
            _owned = owned;
            HtmlUiInputTraceLogger.Event("CURSOR_OWNERSHIP owned=" + owned);

            if (host == null) return;
            host.DispatchToGameThread(() =>
            {
                try
                {
                    if (owned)
                        _setMouseVisible?.Invoke(null, new object[] { true });
                    else
                        _updateMouseVisibility?.Invoke(null, null);
                }
                catch (Exception ex)
                {
                    HtmlUiLogger.Debug("Cursor visibility synchronization failed: " + ex.GetBaseException().Message);
                }
            });
        }

        private static void SetMouseVisiblePrefix(ref bool value)
        {
            if (_owned) value = true;
        }
    }
}
