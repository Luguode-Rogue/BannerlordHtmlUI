using System;
using System.Windows.Forms;

namespace BannerlordHtmlUI
{
    internal sealed class HtmlUiOverlayForm : Form
    {
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYUP = 0x0105;
        private const int VK_ESCAPE = 0x1B;

        private bool _passThrough;

        /// <summary>Raised when ESC is pressed while the overlay (or its child WebView2) has keyboard focus.</summary>
        public event Action EscapePressed;

        public HtmlUiOverlayForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            Width = 1;
            Height = 1;
        }

        public void SetPassThrough(bool enabled)
        {
            _passThrough = enabled;
            Win32.SetNoActivate(Handle, enabled);
        }

        protected override bool ShowWithoutActivation => _passThrough;

        protected override void WndProc(ref Message m)
        {
            const int WM_NCHITTEST = 0x0084;
            const int HTTRANSPARENT = -1;

            if (_passThrough && m.Msg == WM_NCHITTEST)
            {
                m.Result = (IntPtr)HTTRANSPARENT;
                return;
            }

            // ESC fallback: even if the WebView2 DOM never receives the key,
            // close the currently open page from the native layer.
            if (m.Msg == WM_KEYDOWN || m.Msg == WM_SYSKEYDOWN)
            {
                if (((int)m.WParam) == VK_ESCAPE && !_passThrough)
                {
                    EscapePressed?.Invoke();
                    return;
                }
            }

            base.WndProc(ref m);
        }
    }
}
