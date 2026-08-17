using System;
using System.Windows.Forms;

namespace BannerlordHtmlUI
{
    internal sealed class HtmlUiOverlayForm : Form
    {
        private bool _passThrough;

        public Action EscapePressed { get; set; }

        public HtmlUiOverlayForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            // The overlay must follow Bannerlord's Z-order rather than remaining
            // above every other application. TopMost=true caused ALT+TAB to leave
            // the fullscreen HTML UI covering external applications, especially
            // while the game is stopped at a debugger breakpoint.
            TopMost = false;
            Width = 1;
            Height = 1;
            KeyPreview = true;
        }

        public void SetOwner(IntPtr ownerHwnd)
        {
            if (!IsHandleCreated || IsDisposed) return;
            Win32.SetOwner(Handle, ownerHwnd);
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
                    HtmlUiLogger.Info("ESC detected by HtmlUiOverlayForm. Dispatching page close.");
                    EscapePressed?.Invoke();
                }
                catch (Exception ex)
                {
                    HtmlUiLogger.Error("ESC page close dispatch failed.", ex);
                }

                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_NCHITTEST = 0x0084;
            const int HTTRANSPARENT = -1;

            if (_passThrough && m.Msg == WM_NCHITTEST)
            {
                m.Result = (IntPtr)HTTRANSPARENT;
                return;
            }

            base.WndProc(ref m);
        }
    }
}
