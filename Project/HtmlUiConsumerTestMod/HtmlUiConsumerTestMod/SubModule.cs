using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
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
        private const string StressPageName = "consumer.stress";
        private HtmlUiConsumerScope _scope;
        private string _pageId;
        private string _stressPageId;
        private bool _registered;
        private string _logPath;
        private int _counter;
        private string _name = "BannerlordHtmlUI";
        private bool _enabled = true;
        private bool _f12Down;

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            try
            {
                var moduleDirectory = Path.GetDirectoryName(typeof(SubModule).Assembly.Location);
                _logPath = Path.Combine(moduleDirectory ?? ".", "HtmlUiConsumerTestMod.log");
                Log("=== HtmlUiConsumerTestMod loaded ===");
                Log("Assembly=" + typeof(SubModule).Assembly.Location);
                Log("F11/F12/F8/F7 test hooks are active.");
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
                        DefaultInputMode = HtmlUiInputMode.Captured
                    });
                Log("Page registered: " + _pageId);

                _stressPageId = _scope.RegisterPage(
                    new HtmlUiPage(StressPageName, "StressLab/index.html")
                    {
                        ContentRootId = rootId,
                        HotReload = true,
                        DefaultInputMode = HtmlUiInputMode.Captured
                    });
                Log("Stress page registered: " + _stressPageId);

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

                _scope.RegisterCommand("removeNameState", _ =>
                {
                    _scope.RemoveState("name");
                });

                _scope.RegisterCommand("setEnabled", payload =>
                {
                    var value = payload?["value"]?.Value<bool>();
                    if (!value.HasValue) return;
                    _enabled = value.Value;
                    PublishState();
                });

                _scope.RegisterCommand("emitTestEvent", payload =>
                {
                    var message = payload?["message"]?.Value<string>() ?? "Event round-trip";
                    _scope.SendEvent("testEvent", new
                    {
                        message,
                        counter = _counter,
                        utc = DateTime.UtcNow
                    });
                });

                _scope.RegisterCommand("throwCommand", _ =>
                {
                    throw new InvalidOperationException("Intentional consumer command failure.");
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
                        enabled = _enabled
                    });
                });

                _scope.RegisterRequest("getDataDelayed", async payload =>
                {
                    var echo = payload?["echo"]?.Value<string>() ?? string.Empty;
                    await Task.Delay(50).ConfigureAwait(false);
                    return new
                    {
                        mod = OwnerId,
                        echo,
                        counter = _counter,
                        delayed = true
                    };
                });

                _scope.RegisterRequest("getDataCancellable", async (payload, cancellationToken) =>
                {
                    var echo = payload?["echo"]?.Value<string>() ?? string.Empty;
                    await Task.Delay(5000, cancellationToken).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    return new
                    {
                        mod = OwnerId,
                        echo,
                        counter = _counter,
                        cancellable = true
                    };
                });

                _scope.RegisterRequest("throwRequest", payload =>
                {
                    throw new InvalidOperationException("Intentional consumer request failure.");
                });

                PublishState();
                _scope.SetState("loaded", true);
                _registered = true;
                Log("Consumer UI registration completed successfully.");
                Log("M5/M6 diagnostics registered: getDataDelayed, getDataCancellable, emitTestEvent, throwCommand, throwRequest, removeNameState.");
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

            var f12Pressed = (GetAsyncKeyState(0x7B) & 0x8000) != 0;
            if (f12Pressed && !_f12Down)
            {
                _f12Down = true;
                Log("F12 fallback detected. closing current page.");
                CloseCurrent();
            }
            else if (!f12Pressed)
            {
                _f12Down = false;
            }

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

            if (Input.IsKeyPressed(InputKey.F8))
            {
                Log("F8 pressed. opening StressLab page=" + (_stressPageId ?? "<null>"));
                OpenStress();
            }

            if (Input.IsKeyPressed(InputKey.F7))
            {
                Log("F7 pressed. closing StressLab/current page.");
                CloseCurrent();
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
            }
            catch (Exception ex)
            {
                Log("Open ERROR: " + ex);
            }
        }

        private void OpenStress()
        {
            try
            {
                if (!_registered || !HtmlUiService.IsReady)
                {
                    Log("OpenStress skipped: consumer not registered or Framework not ready.");
                    return;
                }

                var result = HtmlUiService.Pages.Open(_stressPageId);
                Log("Stress Pages.Open result=" + result + ", currentPage=" + (HtmlUiService.Pages.CurrentId ?? "<null>"));
            }
            catch (Exception ex)
            {
                Log("OpenStress ERROR: " + ex);
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
            }
            catch (Exception ex)
            {
                Log("Close ERROR: " + ex);
            }
        }

        private void CloseCurrent()
        {
            try
            {
                if (!_registered || !HtmlUiService.IsReady)
                {
                    Log("CloseCurrent skipped: consumer not registered or Framework not ready.");
                    return;
                }

                HtmlUiService.Pages.CloseCurrent();
            }
            catch (Exception ex)
            {
                Log("CloseCurrent ERROR: " + ex);
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
