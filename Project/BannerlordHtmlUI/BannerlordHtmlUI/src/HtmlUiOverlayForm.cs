using System;
using System.Diagnostics;
using System.Windows.Forms;

namespace BannerlordHtmlUI
{
    internal sealed class HtmlUiOverlayForm : Form
    {
        private bool _passThrough;
        private bool _restoreFocusPending;

        public Func<bool> EscapePressed { get; set; }

        public HtmlUiOverlayForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            Width = 1;
            Height = 1;
            KeyPreview = true;
        }

        public void SetPassThrough(bool enabled)
        {
            _passThrough = enabled;
            Win32.SetNoActivate(Handle, enabled);
        }

        protected override bool ShowWithoutActivation => _passThrough;

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if ((keyData & Keys.KeyCode) == Keys.Escape)
            {
                try
                {
                    HtmlUiLogger.Info("ESC detected by HtmlUiOverlayForm.");
                    if (EscapePressed != null && EscapePressed()) return true;
                }
                catch (Exception ex)
                {
                    HtmlUiLogger.Error("ESC page close dispatch failed.", ex);
                    return true;
                }
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_NCHITTEST = 0x0084;
            const int WM_MOUSEACTIVATE = 0x0021;
            const int WM_LBUTTONUP = 0x0202;
            const int WM_RBUTTONUP = 0x0205;
            const int WM_MBUTTONUP = 0x0208;
            const int HTTRANSPARENT = -1;
            const int MA_ACTIVATE = 1;

            if (_passThrough && m.Msg == WM_NCHITTEST)
            {
                m.Result = (IntPtr)HTTRANSPARENT;
                return;
            }

            if (!_passThrough && m.Msg == WM_MOUSEACTIVATE)
            {
                HtmlUiLogger.Info("MouseCaptured WM_MOUSEACTIVATE -> MA_ACTIVATE.");
                m.Result = (IntPtr)MA_ACTIVATE;
                return;
            }

            base.WndProc(ref m);

            if (!_passThrough &&
                (m.Msg == WM_LBUTTONUP || m.Msg == WM_RBUTTONUP || m.Msg == WM_MBUTTONUP) &&
                !_restoreFocusPending)
            {
                RestoreBannerlordKeyboardFocusSoon();
            }
        }

        private void RestoreBannerlordKeyboardFocusSoon()
        {
            _restoreFocusPending = true;
            try
            {
                BeginInvoke(new Action(() =>
                {
                    try
                    {
                        var gameWindow = Process.GetCurrentProcess().MainWindowHandle;
                        if (gameWindow != IntPtr.Zero && Win32.IsWindow(gameWindow))
                        {
                            Win32.SetForegroundWindow(gameWindow);
                            HtmlUiLogger.Info("MouseCaptured mouse release completed; Bannerlord keyboard focus restored.");
                        }
                    }
                    catch (Exception ex)
                    {
                        HtmlUiLogger.Debug("Failed to restore Bannerlord keyboard focus after mouse release: " + ex.GetBaseException().Message);
                    }
                    finally
                    {
                        _restoreFocusPending = false;
                    }
                }));
            }
            catch
            {
                _restoreFocusPending = false;
            }
        }
    }
}