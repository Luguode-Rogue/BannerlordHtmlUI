using System;
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
            TopMost = true;
            Width = 1;
            Height = 1;
            KeyPreview = true;
        }

        public void SetPassThrough(bool enabled)
        {
            _passThrough = enabled;
            // Passive mode must not activate. MouseCaptured deliberately allows
            // activation so the WinForms/WebView2 child gets the native mouse
            // sequence; HtmlUiKeyboardAndDiagnosticsPatch restores Bannerlord
            // keyboard focus on WebView2 MouseUp.
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
                // MouseCaptured needs a real activation so WebView2 receives
                // the native click. Keyboard focus is explicitly returned to
                // Bannerlord from the WebView2 MouseUp hook.
                m.Result = (IntPtr)MA_ACTIVATE;
                return;
            }

            base.WndProc(ref m);
        }
    }
}