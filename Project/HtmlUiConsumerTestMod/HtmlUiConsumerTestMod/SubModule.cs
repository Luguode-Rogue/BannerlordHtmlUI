using System;
using System.IO;
using System.Threading.Tasks;
using BannerlordHtmlUI;
using Newtonsoft.Json.Linq;
using TaleWorlds.InputSystem;
using TaleWorlds.MountAndBlade;

namespace HtmlUiConsumerTestMod
{
    public sealed class SubModule : MBSubModuleBase
    {
        private const string OwnerId = "HtmlUiConsumerTestMod";
        private const string PageName = "consumer.test";
        private const string SecondPageName = "consumer.second";
        private const string SpellPageName = "consumer.spell";
        private HtmlUiConsumerScope _scope;
        private string _pageId;
        private string _secondPageId;
        private string _spellPageId;
        private bool _registered;
        private string _logPath;
        private int _counter;
        private int _secondCounter;
        private int _pushCount;
        private string _name = "BannerlordHtmlUI";
        private bool _enabled = true;

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            try
            {
                var moduleDirectory = Path.GetDirectoryName(typeof(SubModule).Assembly.Location);
                _logPath = Path.Combine(moduleDirectory ?? ".", "HtmlUiConsumerTestMod.log");
                Log("=== HtmlUiConsumerTestMod loaded ===");
                Log("Assembly=" + typeof(SubModule).Assembly.Location);
                Log("F11/F12 test hooks are active.");
                HtmlUiService.OnReady(RegisterUi);
                Log("Registered HtmlUiService.OnReady callback.");
            }
            catch (Exception ex)
            {
                Log("OnSubModuleLoad ERROR: " + ex);
            }
        }

        private void RegisterUi()
        {
            try
            {
                Log("HtmlUiService.OnReady fired. IsReady=" + HtmlUiService.IsReady);
                if (_registered) return;

                var moduleDirectory = Path.GetDirectoryName(typeof(SubModule).Assembly.Location);
                var uiRoot = Path.Combine(moduleDirectory ?? ".", "UI");
                Log("UI root=" + uiRoot);

                _scope = HtmlUiService.CreateScope(OwnerId);
                var rootId = _scope.RegisterContentRoot("ui", uiRoot);
                Log("ContentRoot registered: " + rootId);

                _pageId = _scope.RegisterPage(
                    new HtmlUiPage(PageName, "Test/index.html")
                    {
                        ContentRootId = rootId,
                        HotReload = true,
                        DefaultInputMode = HtmlUiInputMode.Captured,
                        Opened = () => { Log("PAGE CALLBACK Opened: " + PageName); _scope.SendEvent("pageOpened", new { pageId = _pageId }); },
                        Closed = () => { Log("PAGE CALLBACK Closed: " + PageName); _scope.SendEvent("pageClosed", new { pageId = _pageId }); }
                    });
                Log("Page registered: " + _pageId);

                // Second page: non-fullscreen transparent overlay HUD (bottom-right).
                // Transparent=true makes the WebView2 background fully transparent so the
                // game shows through the semi-transparent HUD panel.
                _secondPageId = _scope.RegisterPage(
                    new HtmlUiPage(SecondPageName, "Second/index.html")
                    {
                        ContentRootId = rootId,
                        HotReload = true,
                        DefaultInputMode = HtmlUiInputMode.Passive,
                        OverlayWidth = 360,
                        OverlayHeight = 260,
                        Transparent = true,
                        Opened = () => { Log("PAGE CALLBACK Opened: " + SecondPageName); },
                        Closed = () => { Log("PAGE CALLBACK Closed: " + SecondPageName); }
                    });
                Log("Second page registered: " + _secondPageId);

                // Third page: full-screen interactive Spell VM Lab (Captured input).
                // NOTE: WebView2 transparent background (Transparent=true) is NOT supported
                // in this environment — setting it corrupts rendering for ALL pages (including
                // ones opened later). Kept opaque for stability. Transparent overlay requires
                // a reliable WebView2 transparent pipeline (e.g. Composition Controller).
                _spellPageId = _scope.RegisterPage(
                    new HtmlUiPage(SpellPageName, "SpellLab/index.html")
                    {
                        ContentRootId = rootId,
                        HotReload = true,
                        DefaultInputMode = HtmlUiInputMode.Captured,
                        Opened = () => { Log("PAGE CALLBACK Opened: " + SpellPageName); },
                        Closed = () => { Log("PAGE CALLBACK Closed: " + SpellPageName); }
                    });
                Log("Spell page registered: " + _spellPageId);

                _scope.RegisterCommand("openFirst", _ =>
                {
                    Log("Command openFirst: switching back to first page.");
                    HtmlUiService.Pages.Open(_pageId);
                });
                _scope.RegisterCommand("openSecond", _ =>
                {
                    Log("Command openSecond: switching to second page.");
                    HtmlUiService.Pages.Open(_secondPageId);
                });
                _scope.RegisterCommand("openSpell", _ =>
                {
                    Log("Command openSpell: switching to spell lab page.");
                    HtmlUiService.Pages.Open(_spellPageId);
                });
                _scope.RegisterCommand("reload", _ =>
                {
                    Log("Command reload: reloading current page.");
                    HtmlUiService.ReloadPage();
                });
                _scope.RegisterCommand("secondIncrement", _ =>
                {
                    _secondCounter++;
                    _scope.SetState("secondCount", _secondCounter);
                    Log("Command secondIncrement: secondCount=" + _secondCounter);
                });

                _scope.RegisterCommand("increment", _ =>
                {
                    _counter++;
                    PublishState();
                    _scope.SendEvent("counterChanged", new { value = _counter });
                });

                _scope.RegisterCommand("setName", payload =>
                {
                    var value = payload?["value"]?.Value<string>();
                    if (value == null) return;
                    _name = value;
                    PublishState();
                });

                _scope.RegisterCommand("setEnabled", payload =>
                {
                    var value = payload?["value"]?.Value<bool>();
                    if (!value.HasValue) return;
                    _enabled = value.Value;
                    PublishState();
                });

                _scope.RegisterRequest("getData", payload =>
                {
                    var echo = payload?["echo"]?.Value<string>() ?? string.Empty;
                    return Task.FromResult<object>(new
                    {
                        mod = OwnerId,
                        echo,
                        counter = _counter,
                        name = _name,
                        enabled = _enabled,
                        // Read back a scoped state key using only the public scope API.
                        loaded = _scope.GetState("loaded")
                    });
                });

                // C# -> JS reverse link: JS calls this command, C# does work and
                // actively pushes a result back to JS via SendEvent.
                _scope.RegisterCommand("pushEvent", payload =>
                {
                    var msg = payload?["message"]?.Value<string>() ?? "push";
                    var n = _pushCount++;
                    Log("pushEvent command: " + msg + " #" + n);
                    _scope.SendEvent("serverPush", new { count = n, message = msg });
                });

                PublishState();
                _scope.SetState("loaded", true);
                _registered = true;
                Log("Consumer UI registration completed successfully.");
            }
            catch (Exception ex)
            {
                Log("RegisterUi ERROR: " + ex);
            }
        }

        private void PublishState()
        {
            _scope.SetState("counter", _counter);
            _scope.SetState("name", _name);
            _scope.SetState("enabled", _enabled);
        }

        protected override void OnApplicationTick(float dt)
        {
            base.OnApplicationTick(dt);

            if (Input.IsKeyPressed(InputKey.F11))
            {
                Log("F11 pressed. registered=" + _registered + ", frameworkReady=" + HtmlUiService.IsReady + ", page=" + (_pageId ?? "<null>"));
                Open();
            }

            if (Input.IsKeyPressed(InputKey.F12))
            {
                Log("F12 pressed. registered=" + _registered + ", frameworkReady=" + HtmlUiService.IsReady + ", page=" + (_pageId ?? "<null>"));
                Close();
            }
        }

        private void Open()
        {
            try
            {
                if (!_registered || !HtmlUiService.IsReady)
                {
                    Log("Open skipped: consumer not registered or Framework not ready.");
                    return;
                }

                var result = HtmlUiService.Pages.Open(_pageId);
                Log("Pages.Open result=" + result + ", currentPage=" + (HtmlUiService.Pages.CurrentId ?? "<null>"));
                HtmlUiService.CaptureInput();
            }
            catch (Exception ex)
            {
                Log("Open ERROR: " + ex);
            }
        }

        private void Close()
        {
            try
            {
                if (!_registered || !HtmlUiService.IsReady)
                {
                    Log("Close skipped: consumer not registered or Framework not ready.");
                    return;
                }

                HtmlUiService.Pages.Close(_pageId);
                Log("Pages.Close result=");
                // Do NOT call ReleaseInput() here: CloseCurrent() -> Hide() already sets the
                // window to Hidden. Calling ReleaseInput() afterwards switches to Passive and
                // re-flags _requestedVisible = true, which makes FollowBannerlordWindow show the
                // window again -> "F12 closes but the page stays visible".
            }
            catch (Exception ex)
            {
                Log("Close ERROR: " + ex);
            }
        }

        protected override void OnSubModuleUnloaded()
        {
            try
            {
                Log("=== HtmlUiConsumerTestMod unloading ===");
                _scope?.Dispose();
                _scope = null;
                _registered = false;
            }
            catch (Exception ex)
            {
                Log("Unload ERROR: " + ex);
            }

            base.OnSubModuleUnloaded();
        }

        private void Log(string message)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_logPath)) return;
                File.AppendAllText(_logPath,
                    "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "] [" + Environment.CurrentManagedThreadId + "] " + message + Environment.NewLine);
            }
            catch { }
        }
    }
}
