using System;
using System.Diagnostics;
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
            // Do not use TopMost. The HTML overlay must never stay above unrelated
            // applications after Bannerlord loses foreground focus.
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
                // Establish Bannerlord as the Win32 owner before the overlay is
                // shown. This keeps the overlay in Bannerlord's owned-window Z-order
                // and makes it subordinate to Bannerlord without requiring a game
                // thread tick. This is important when a debugger suspends managed
                // threads while the user ALT+TABs to another application.
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
