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

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            try
            {
                // Keep the overlay immediately above Bannerlord while it is visible,
                // but never activate it. This matters for borderless/fullscreen game
                // windows where an owned WinForms window can otherwise fall behind
                // the render surface after navigation or focus transitions.
                Win32.BringWindowAboveOwnerWithoutActivate(Handle);
            }
            catch (Exception ex)
            {
                HtmlUiLogger.Debug("Failed to raise HtmlUI overlay without activation: " + ex.GetBaseException().Message);
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