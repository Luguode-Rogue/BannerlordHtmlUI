using System;
using BannerlordHtmlUI;

namespace HtmlUiConsumerTestMod
{
    internal sealed class HtmlUiConsumerLifecycleStress
    {
        private const int TargetRounds = 20;
        private const int StepDelayTicks = 20;

        private readonly HtmlUiConsumerScope _scope;
        private readonly string _pageId;
        private readonly string _stressPageId;
        private readonly Action<string> _log;
        private int _round;
        private int _step;
        private int _waitTicks;
        private bool _running;
        private int _successes;
        private int _failures;

        public HtmlUiConsumerLifecycleStress(HtmlUiConsumerScope scope, string pageId, string stressPageId, Action<string> log)
        {
            _scope = scope;
            _pageId = pageId;
            _stressPageId = stressPageId;
            _log = log;
        }

        public bool IsRunning => _running;

        public void Start()
        {
            if (_running) return;
            if (_scope == null || !HtmlUiService.IsReady)
            {
                _log?.Invoke("Lifecycle stress skipped: Framework/Consumer is not ready.");
                return;
            }

            _round = 0;
            _step = 0;
            _waitTicks = 0;
            _successes = 0;
            _failures = 0;
            _running = true;
            _log?.Invoke("=== Lifecycle Stress START: " + TargetRounds + " rounds ===");
        }

        public void Tick()
        {
            if (!_running) return;
            if (!HtmlUiService.IsReady)
            {
                Finish("Framework became unavailable.");
                return;
            }

            if (_waitTicks > 0)
            {
                _waitTicks--;
                return;
            }

            try
            {
                switch (_step)
                {
                    case 0:
                        _round++;
                        Open(_pageId, "open test");
                        _step = 1;
                        break;
                    case 1:
                        if (HtmlUiService.Pages.CurrentId == null) return;
                        if (HtmlUiService.Pages.Reload())
                        {
                            _log?.Invoke("Lifecycle stress round " + _round + ": reload test");
                            _step = 2;
                            _waitTicks = StepDelayTicks;
                        }
                        else
                        {
                            Fail("reload test rejected");
                            AdvanceOrFinish();
                        }
                        break;
                    case 2:
                        Open(_stressPageId, "open stress");
                        _step = 3;
                        break;
                    case 3:
                        CloseCurrent();
                        _step = 4;
                        _waitTicks = StepDelayTicks;
                        break;
                    case 4:
                        Open(_pageId, "reopen test");
                        _step = 5;
                        break;
                    case 5:
                        CloseCurrent();
                        _step = 6;
                        _waitTicks = StepDelayTicks;
                        break;
                    case 6:
                        Success();
                        AdvanceOrFinish();
                        break;
                }
            }
            catch (Exception ex)
            {
                Fail("exception: " + ex.GetBaseException().Message);
                AdvanceOrFinish();
            }
        }

        public void Stop()
        {
            if (!_running) return;
            try { HtmlUiService.Pages.CloseCurrent(); } catch { }
            Finish("Stopped by user.");
        }

        private void Open(string id, string label)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                Fail(label + ": missing page id");
                return;
            }

            if (!HtmlUiService.Pages.Open(id))
            {
                Fail(label + ": Pages.Open returned false for " + id);
                return;
            }

            _log?.Invoke("Lifecycle stress round " + _round + ": " + label + " -> " + id);
            _waitTicks = StepDelayTicks;
        }

        private void CloseCurrent()
        {
            var before = HtmlUiService.Pages.CurrentId;
            HtmlUiService.Pages.CloseCurrent();
            _log?.Invoke("Lifecycle stress round " + _round + ": close current (before=" + (before ?? "<null>") + ", after=" + (HtmlUiService.Pages.CurrentId ?? "<null>") + ")");
        }

        private void Success()
        {
            _successes++;
            _log?.Invoke("Lifecycle stress round " + _round + ": PASS");
        }

        private void Fail(string message)
        {
            _failures++;
            _log?.Invoke("Lifecycle stress round " + _round + ": FAIL | " + message);
        }

        private void AdvanceOrFinish()
        {
            if (_round >= TargetRounds)
            {
                Finish("Target rounds complete.");
                return;
            }

            _step = 0;
            _waitTicks = StepDelayTicks;
        }

        private void Finish(string reason)
        {
            _running = false;
            var diagnostics = HtmlUiDiagnostics.Snapshot();
            _log?.Invoke("=== Lifecycle Stress END | reason=" + reason
                + " | rounds=" + _round
                + " | pass=" + _successes
                + " | fail=" + _failures
                + " | currentPage=" + (HtmlUiService.Pages.CurrentId ?? "<null>")
                + " | stateCount=" + diagnostics.StateCount
                + " | pageCount=" + diagnostics.PageCount
                + " | contentRootCount=" + diagnostics.ContentRootCount
                + " | bridgeCommandCount=" + diagnostics.BridgeCommandCount
                + " | bridgeRequestCount=" + diagnostics.BridgeRequestCount
                + " | activeRequestCount=" + diagnostics.ActiveRequestCount
                + " | navigationInProgress=" + diagnostics.NavigationInProgress
                + " | webViewReady=" + diagnostics.WebViewReady
                + " ===");
        }
    }
}
