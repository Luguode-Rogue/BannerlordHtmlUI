using System;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.InputSystem;

namespace BannerlordHtmlUI
{
    /// <summary>
    /// Blocks Bannerlord from seeing input that the HTML overlay owns.
    ///
    /// Bannerlord polls input through <c>TaleWorlds.InputSystem.Input</c> instead of relying on
    /// which window received the Win32 message. That means making the overlay non-transparent is
    /// not enough: the game would still observe the same click and act on it. This blocker is the
    /// other half, and it is the only place allowed to suppress game-side input.
    /// </summary>
    internal static class HtmlUiInputBlocker
    {
        private const string HarmonyId = "BannerlordHtmlUI.InputBlocker";

        private static readonly object Sync = new object();
        private static Harmony _harmony;
        private static bool _installed;
        private static volatile bool _blockMouse;
        private static volatile bool _blockKeyboard;

        /// <summary>Set to false to disable game input suppression entirely (diagnostics only).</summary>
        public static bool Enabled { get; set; } = true;

        internal static bool IsBlockingMouse => _blockMouse;
        internal static bool IsBlockingKeyboard => _blockKeyboard;

        public static void Install()
        {
            lock (Sync)
            {
                if (_installed) return;

                var inputType = typeof(Input);
                var isKeyDown = AccessTools.Method(inputType, nameof(Input.IsKeyDown));
                var isKeyPressed = AccessTools.Method(inputType, nameof(Input.IsKeyPressed));
                var isKeyReleased = AccessTools.Method(inputType, nameof(Input.IsKeyReleased));
                // Confirmed against decompiled 1.5.0 sources: all four are single-parameter static
                // methods with no overloads. IsKeyDownImmediate bypasses the game's edge tracking
                // and must be blocked too, or the suppression can be circumvented.
                var isKeyDownImmediate = AccessTools.Method(inputType, "IsKeyDownImmediate");

                if (isKeyDown == null || isKeyPressed == null || isKeyReleased == null)
                    throw new MissingMethodException("TaleWorlds.InputSystem.Input polling methods are unavailable.");

                _harmony = new Harmony(HarmonyId);
                _harmony.Patch(isKeyDown, prefix: new HarmonyMethod(typeof(HtmlUiInputBlocker), nameof(IsKeyDownPrefix)));
                _harmony.Patch(isKeyPressed, prefix: new HarmonyMethod(typeof(HtmlUiInputBlocker), nameof(IsKeyPressedPrefix)));
                _harmony.Patch(isKeyReleased, prefix: new HarmonyMethod(typeof(HtmlUiInputBlocker), nameof(IsKeyReleasedPrefix)));

                if (isKeyDownImmediate != null)
                    _harmony.Patch(isKeyDownImmediate, prefix: new HarmonyMethod(typeof(HtmlUiInputBlocker), nameof(IsKeyDownPrefix)));

                _installed = true;
                HtmlUiLogger.Info("Bannerlord input blocker installed.");
            }
        }

        public static void Uninstall()
        {
            lock (Sync)
            {
                if (!_installed) return;
                try { _harmony?.UnpatchAll(HarmonyId); }
                catch (Exception ex) { HtmlUiLogger.Debug("Input blocker uninstall failed: " + ex.GetBaseException().Message); }
                finally
                {
                    _harmony = null;
                    _installed = false;
                    _blockMouse = false;
                    _blockKeyboard = false;
                }
            }
        }

        /// <summary>
        /// Called only by the input controller when it applies a mode.
        /// Passive and Hidden never block: the game must stay fully playable.
        /// </summary>
        internal static void SetBlocking(bool mouse, bool keyboard)
        {
            if (_blockMouse == mouse && _blockKeyboard == keyboard) return;
            _blockMouse = mouse;
            _blockKeyboard = keyboard;
            HtmlUiInputTraceLogger.Event("INPUT_BLOCK mouse=" + mouse + " keyboard=" + keyboard);
            HtmlUiLogger.Info("Bannerlord input blocking: mouse=" + mouse + " keyboard=" + keyboard);
        }

        private static bool ShouldBlock(InputKey key)
        {
            if (!Enabled) return false;
            if (!_blockMouse && !_blockKeyboard) return false;
            return IsMouseKey(key) ? _blockMouse : _blockKeyboard;
        }

        private static bool IsMouseKey(InputKey key)
        {
            return key == InputKey.LeftMouseButton
                || key == InputKey.RightMouseButton
                || key == InputKey.MiddleMouseButton
                || key == InputKey.MouseScrollUp
                || key == InputKey.MouseScrollDown;
        }

        private static bool IsKeyDownPrefix(InputKey key, ref bool __result)
        {
            if (!ShouldBlock(key)) return true;
            __result = false;
            return false;
        }

        private static bool IsKeyPressedPrefix(InputKey key, ref bool __result)
        {
            if (!ShouldBlock(key)) return true;
            __result = false;
            return false;
        }

        private static bool IsKeyReleasedPrefix(InputKey key, ref bool __result)
        {
            if (!ShouldBlock(key)) return true;
            __result = false;
            return false;
        }
    }
}
