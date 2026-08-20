using System;
using System.Diagnostics;
using System.Windows.Forms;

namespace BannerlordHtmlUI
{
    internal sealed class HtmlUiOverlayForm : Form
    {
        private bool _passThrough;

        public Func<bool> EscapePressed { get; set; }

        public HtmlUiOverlayForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            // Keep the overlay in Bannerlord's owned-window Z-order. A permanent TopMost window
            // can cover unrelated applications after ALT-TAB or when the game is suspended.
            TopMost = false;
            Width = 1;
            Height = 1;
            KeyPreview = true;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);

            try
            {
                var ownerHwnd = Process.GetCurrentProcess().MainWindowHandle;
                if (ownerHwnd != IntPtr.Zero && ownerHwnd != Handle && Win32.IsWindow(ownerHwnd))
                    Win32.SetOwner(Handle, ownerHwnd);
            }
            catch (Exception ex)
            {
                HtmlUiLogger.Debug("Failed to bind HtmlUI overlay owner window: " + ex.GetBaseException().Message);
            }
        }

        public void SetOwner(IntPtr ownerHwnd)
        {
            if (!IsHandleCreated || IsDisposed) return;
            try
            {
                Win32.SetOwner(Handle, ownerHwnd);
            }
            catch (Exception ex)
            {
                HtmlUiLogger.Debug("Failed to update HtmlUI overlay owner: " + ex.GetBaseException().Message);
            }
        }

        public void SetPassThrough(bool enabled)
        {
            _passThrough = enabled;
            // Passive mode must not activate. MouseCaptured deliberately allows activation so
            // the WinForms/WebView2 child receives a native mouse sequence.
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
            const int HTTRANSPARENT = -1;
            const int MA_ACTIVATE = 1;

            if (_passThrough && m.Msg == WM_NCHITTEST)
            {
                m.Result = (IntPtr)HTTRANSPARENT;
                return;
            }

            if (!_passThrough && m.Msg == WM_MOUSEACTIVATE)
            {
                // MouseCaptured must activate the overlay so WebView2 can receive the click.
                // Keyboard focus is returned to Bannerlord after mouse release by the WebView hook.
                m.Result = (IntPtr)MA_ACTIVATE;
                return;
            }

            base.WndProc(ref m);
        }
    }
}
