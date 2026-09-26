using System;

namespace BannerlordHtmlUI
{
    /// <summary>
    /// The single place that decides host visibility and input mode.
    ///
    /// Policy (v1, PageDominant):
    ///   - While a page is open the page owns the host. Surfaces are suppressed but keep their
    ///     definitions and state, so they restore automatically when the page closes.
    ///   - With no page open, surfaces own the host. Visibility and input are derived from the
    ///     aggregated demand of all visible surfaces.
    /// </summary>
    internal sealed class HtmlUiInputCoordinator
    {
        private readonly HtmlUiHost _host;
        private readonly object _sync = new object();
        private bool _pageActive;

        internal HtmlUiInputCoordinator(HtmlUiHost host)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
        }

        internal bool IsPageActive { get { lock (_sync) return _pageActive; } }

        internal void OnPageOpened()
        {
            lock (_sync) _pageActive = true;
            _host.Surfaces.SetSuppressedByPage(true);
        }

        internal void OnPageClosed()
        {
            // Page callbacks run synchronously and may open the next page before the
            // previous close notification unwinds. That newer page owns suppression.
            if (_host.Pages.CurrentId != null) return;
            bool wasPageActive;
            lock (_sync)
            {
                wasPageActive = _pageActive;
                _pageActive = false;
            }
            // Hiding the overlay does not unload its document. Retire the closed
            // page now, even with no visible surfaces, so its scripts/watchers do
            // not survive until the first battle HUD state arrives.
            if (wasPageActive) _host.NavigateToShell();
            _host.Surfaces.SetSuppressedByPage(false);
            if (_host.Pages.CurrentId != null) return;
            ApplySurfaceMode("page-closed");
        }

        internal void OnSurfacesChanged()
        {
            lock (_sync) { if (_pageActive) return; }
            ApplySurfaceMode("surfaces-changed");
        }

        private void ApplySurfaceMode(string reason)
        {
            try
            {
                if (_host.Pages.CurrentId != null) return;
                var aggregate = _host.Surfaces.Aggregate;
                var mode = aggregate.HasVisible ? aggregate.EffectiveInputMode : HtmlUiInputMode.Hidden;

                if (aggregate.HasVisible && !_host.IsShellActive)
                {
                    HtmlUiLogger.Info("Surface shell requested: reason=" + reason + " surfaces=" + _host.Surfaces.Count);
                    _host.NavigateToShell();
                }

                HtmlUiLogger.Info("Surface input resolved: reason=" + reason +
                    " mode=" + mode +
                    " owner=" + (aggregate.InputOwnerId ?? "<none>") +
                    " hasVisible=" + aggregate.HasVisible);
                _host.SetInputMode(mode);
            }
            catch (Exception ex)
            {
                HtmlUiLogger.Error("Failed to apply aggregated surface input mode.", ex);
            }
        }
    }
}
