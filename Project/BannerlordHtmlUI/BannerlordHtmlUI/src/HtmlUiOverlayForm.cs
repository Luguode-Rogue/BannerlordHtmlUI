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
            try { Win32.SetOwner(Handle, ownerHwnd); }
            catch (Exception ex) { HtmlUiLogger.Debug("Failed to update HtmlUI overlay owner: " + ex.GetBaseException().Message); }
        }

        public void SetPassThrough(bool enabled)
        {
            _passThrough = enabled;
            Win32.SetNoActivate(Handle, enabled);
        }

        protected override bool ShowWithoutActivation => _passThrough;

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // Hidden and Passive modes never own keyboard input. This is a native-side
            // fail-safe even if focus state briefly survives a mode transition.
            if (!Visible || _passThrough)
                return base.ProcessCmdKey(ref msg, keyData);

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
                m.Result = (IntPtr)MA_ACTIVATE;
                return;
            }

            base.WndProc(ref m);
        }
    }
}
