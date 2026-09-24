using System.Diagnostics;
using System.Drawing.Imaging;
using System.Text;
using System.Text.Json;
using KioskBrowser.Abstractions;
using KioskBrowser.Shared;
using KioskBrowser.Voice;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace KioskBrowser;

/// <summary>
/// Kiosk 主窗口：全屏置顶无边框 + WebView2 + 设置入口 + 软键盘 + 模拟登录 + 心跳。
/// 专业版能力（远程管理/SSO 引擎）经 KioskBrowser.Pro.dll / KioskBrowser.Sso.dll 动态加载。
/// </summary>
public sealed class MainForm : Form, IProHost
{
    private readonly KioskConfig _config;
    private readonly WebView2 _web = new();
    private readonly Panel _keyboardHost = new();
    private readonly OnScreenKeyboard _keyboard;
    private readonly CandidateBar _candBar = new();
    private readonly KeyboardHook _hook = new();
    private readonly TabTipKiller _tabTip = new();
    private readonly HeartbeatClient _heartbeat = new();
    private readonly ZoneTapTracker _zone;
    private readonly System.Windows.Forms.Timer _hideKbTimer = new() { Interval = 350 };
    private readonly System.Windows.Forms.Timer _heartbeatTimer = new();

    private Panel? _settingsMask;
    private SettingsPanel? _settings;
    private bool _settingsOpen;
    private bool _webReady;
    private bool _allowClose;
    private int _loginFailures;
    private int _ticketFailures;
    private bool _ticketRunning;
    private bool _ticketFallbackNotified;
    private VoiceInputController? _voice;
    private IProPlugin? _pro;

    /// <summary>票据 SSO 模式（auth.type=ticket）。</summary>
    private bool TicketMode => _config.Auth.Type.Equals("ticket", StringComparison.OrdinalIgnoreCase);
    private int TicketMaxFailures => Math.Max(1, _config.Auth.Ticket.MaxFailures);
    /// <summary>连续失败超限 → 已降级模拟登录。</summary>
    private bool TicketFallenBack => _ticketFailures >= TicketMaxFailures;

    public MainForm(KioskConfig config)
    {
        _config = config;

        if (config.Browser.KioskMode)
        {
            // 霸屏：全屏锁定
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Maximized;
            TopMost = config.Browser.AlwaysOnTop;
            ShowInTaskbar = false;
        }
        else
        {
            // 普通窗口（测试/调试模式）：可调大小、可关闭、显示任务栏
            FormBorderStyle = FormBorderStyle.Sizable;
            WindowState = FormWindowState.Normal;
            StartPosition = FormStartPosition.CenterScreen;
            var wa = Screen.PrimaryScreen!.WorkingArea;
            Size = new Size(Math.Min(DpiHelper.S(1280), wa.Width - DpiHelper.S(80)),
                            Math.Min(DpiHelper.S(800), wa.Height - DpiHelper.S(80)));
            TopMost = false;
            ShowInTaskbar = true;        }
        BackColor = Color.Black;
        Text = "KioskBrowser";

        _web.Dock = DockStyle.Fill;
        Controls.Add(_web);

        // 软键盘宿主：吸附底部，隐藏时 WebView 自动占满
        _keyboard = new OnScreenKeyboard(config.Keyboard.EnableChinese, config.Speech.Enabled);
        _keyboard.Dock = DockStyle.Fill;
        _keyboard.KeyPressed += OnKeyboardKey;
        _keyboard.HideRequested += (s, e) => SetKeyboardVisible(false);
        _keyboard.CompositionChanged += (s, e) => UpdateCandidateBar();
        _keyboard.VoiceStart += OnVoiceStart;
        _keyboard.VoiceStop += OnVoiceStop;
        _keyboardHost.Dock = DockStyle.Bottom;
        // 高度在 SetKeyboardVisible 时按当前窗口高度计算（霸屏=全屏，窗口模式也正确）
        _keyboardHost.Controls.Add(_keyboard);
        _keyboardHost.Visible = false;
        Controls.Add(_keyboardHost);
        _keyboardHost.BringToFront();

        // 候选词栏：悬浮在键盘上方，覆盖页面内容，不挤压键盘布局
        _candBar.CandidatePicked += (s, idx) => _keyboard.PickCandidate(idx);
        Controls.Add(_candBar);
        _candBar.BringToFront();

        // 普通窗口模式：右上角显示「设置」按钮（霸屏模式保持隐蔽入口）
        if (!config.Browser.KioskMode)
        {
            var btnSettings = new Button
            {
                Text = "⚙ 设置",
                Width = DpiHelper.S(96),
                Height = DpiHelper.S(36),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                BackColor = Color.FromArgb(50, 110, 200),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold)
            };
            btnSettings.Location = new Point(
                Width - btnSettings.Width - DpiHelper.S(12), DpiHelper.S(8));
            btnSettings.Click += (s, e) => OpenSettings();
            Controls.Add(btnSettings);
            btnSettings.BringToFront();
        }

        var se = config.SettingsEntry;
        _zone = new ZoneTapTracker(se.TapCount, se.TimeWindowMs, se.MaxGapMs,
            se.DeviationPx, se.MaxAttempts, se.LockoutSeconds);
        _zone.Triggered += (s, e) => OpenSettings();

        _hideKbTimer.Tick += (s, e) =>
        {
            _hideKbTimer.Stop();
            if (!_settingsOpen) SetKeyboardVisible(false); // 设置页期间键盘必须保持
        };

        _heartbeatTimer.Interval = Math.Max(1000, config.Watchdog.HeartbeatIntervalMs);
        _heartbeatTimer.Tick += async (s, e) => await _heartbeat.SendHeartbeatAsync();

        _hook.AllowFunctionKeys = config.DevMode;

        Load += async (s, e) => await InitAsync();
        FormClosing += OnFormClosing;
        Resize += (s, e) =>
        {
            if (_keyboardHost.Visible) SetKeyboardVisible(true); // 重算键盘高度
            if (_settingsMask != null && _config.Keyboard.Enabled)
                _settingsMask.Height = _keyboardHost.Top;
        };
    }

    /// <summary>普通窗口模式快捷键：Ctrl+Alt+S 打开设置。</summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (!_config.Browser.KioskMode && keyData == (Keys.Control | Keys.Alt | Keys.S))
        {
            OpenSettings();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private async Task InitAsync()
    {
        try
        {
            var env = await CoreWebView2Environment.CreateAsync(null, AppPaths.WebViewDataDir);
            await _web.EnsureCoreWebView2Async(env);
            var cw = _web.CoreWebView2;
            var st = cw.Settings;
            st.AreDefaultContextMenusEnabled = false;   // 禁用右键菜单
            st.AreDevToolsEnabled = _config.DevMode;
            st.AreBrowserAcceleratorKeysEnabled = false;
            st.IsStatusBarEnabled = false;
            st.IsZoomControlEnabled = false;
            st.IsPasswordAutosaveEnabled = false;
            st.IsGeneralAutofillEnabled = false;

            await cw.AddScriptToExecuteOnDocumentCreatedAsync(
                InjectedScripts.FocusAndZone(_config.SettingsEntry.ZoneSize));
            cw.WebMessageReceived += OnWebMessage;
            cw.NavigationCompleted += OnNavigationCompleted;
            cw.NavigationStarting += OnNavigationStarting;
            cw.NewWindowRequested += (s, e) =>
            {
                e.Handled = true;
                try { cw.Navigate(e.Uri); } catch { /* ignore */ }
            };

            _webReady = true;
            if (TicketMode) _ = RunTicketAuthAsync("startup");
            else NavigateHome();

            // 拼音引擎 + 热词文件 + 语音模型（后台线程，不卡首屏）
            _ = Task.Run(async () =>
            {
                if (_config.Keyboard.EnableChinese || _config.Speech.Enabled)
                    PinyinEngine.Instance.Configure(
                        _config.Keyboard.Vocabularies, _config.Keyboard.CustomVocabulary);
                if (!_config.Speech.Enabled) return;

                // 热词 = 领域词库 + 自定义词库（非通用词库）
                HotwordManager.WriteHotwordsFile(PinyinEngine.Instance.BoostWords);

                // 模型就绪后加载语音控制器
                var ok = await ModelManager.EnsureAsync(_config.Speech.ModelSize,
                    (file, done, total) => Logger.Debug(
                        $"model {file}: {done:F1}/{(total > 0 ? total.ToString("F1") : "?")}MB"));
                if (!ok)
                {
                    Logger.Error("speech model not ready (download failed)");
                    _ = WebhookAlerter.PostAsync(_config.Watchdog.WebhookUrl,
                        "model-download-failed", "语音模型下载失败");
                    return;
                }
                try
                {
                    var voice = new VoiceInputController(_config.Speech);
                    voice.StatusChanged += t => BeginInvoke(() =>
                    {
                        ShowCandBarAboveKeyboard();
                        _candBar.SetStatus(t);
                    });
                    voice.FinalResult += t => BeginInvoke(async () =>
                    {
                        if (t.Length > 0)
                        {
                            _candBar.Visible = false;
                            OnKeyboardKey(this, new KioskKeyEventArgs { Text = t });
                        }
                        else
                        {
                            ShowCandBarAboveKeyboard();
                            _candBar.SetStatus("未识别到语音，请再试");
                            await Task.Delay(1500);
                            if (_keyboard.Composition.Length == 0)
                                _candBar.Visible = false;
                        }
                    });
                    _voice = voice;
                    Logger.Info("voice input ready");
                }
                catch (Exception ex)
                {
                    Logger.Error("voice init failed", ex);
                }
            });

            // 键盘钩子只在霸屏模式下启用（普通窗口模式便于测试）
            if (_config.Browser.KioskMode && _config.Browser.BlockSystemKeys) _hook.Install();

            // 专业版插件（远程管理，有 KioskBrowser.Pro.dll 才启用）
            StartPro();
            _tabTip.Start();
            _heartbeatTimer.Start();
            Logger.Info($"initialized, home={_config.Browser.HomeUrl}");
        }
        catch (Exception ex)
        {
            Logger.Error("init failed", ex);
            ShowFatalError($"初始化失败：{ex.Message}\n请确认已安装 WebView2 Runtime。");
        }
    }

    private void NavigateHome()
    {
        var url = _config.Browser.HomeUrl;
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            _web.CoreWebView2.Navigate(url);
        }
        else
        {
            ShowErrorPage("配置无效", $"目标 URL 无效：{url}<br/>请通过左上角连击进入设置进行配置。");
        }
    }

    // ---------------- 导航限制 ----------------

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        var whitelist = _config.Browser.NavigationWhitelist;
        if (whitelist.Count == 0) return;
        try
        {
            var host = new Uri(e.Uri).Host;
            var allowed = whitelist.Any(p =>
                host.Equals(p, StringComparison.OrdinalIgnoreCase)
                || host.EndsWith("." + p, StringComparison.OrdinalIgnoreCase));
            if (!allowed)
            {
                Logger.Warning($"navigation blocked by whitelist: {e.Uri}");
                e.Cancel = true;
            }
        }
        catch
        {
            e.Cancel = true;
        }
    }

    // ---------------- 页面消息（连击 / 焦点 / 登录结果） ----------------

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var type = doc.RootElement.GetProperty("type").GetString();
            switch (type)
            {
                case "zonetap" when _config.SettingsEntry.Enabled && !_settingsOpen:
                    _zone.Tap(doc.RootElement.GetProperty("x").GetInt32(),
                              doc.RootElement.GetProperty("y").GetInt32());
                    break;
                case "focus" when _config.Keyboard.Enabled:
                    _hideKbTimer.Stop();
                    SetKeyboardVisible(true);
                    break;
                case "blur":
                    _hideKbTimer.Stop();
                    _hideKbTimer.Start();
                    break;
            }
        }
        catch { /* 非预期消息忽略 */ }
    }

    // ---------------- 票据 SSO（协议级免登） ----------------

    /// <summary>
    /// 取票并建立会话。连续失败 MaxFailures 次后降级模拟登录并 Webhook 告警。
    /// 取票成功后是否真正登录，由 OnNavigationCompleted 按 ReAuthWhenUrlContains 判定。
    /// </summary>
    private async Task RunTicketAuthAsync(string reason)
    {
        if (_ticketRunning || !_webReady || !TicketMode) return;
        _ticketRunning = true;
        try
        {
            // SSO 引擎为专业版组件：未安装时不空转重试，直接降级模拟登录
            if (!SsoBridge.Available)
            {
                Logger.Error("ticket sso unavailable: KioskBrowser.Sso.dll 未安装（专业版组件），降级模拟登录");
                _ticketFailures = TicketMaxFailures;
                NotifyTicketFallback();
                NavigateHome();
                return;
            }
            while (!TicketFallenBack)
            {
                try
                {
                    Logger.Info($"ticket sso acquire ({reason}, attempt {_ticketFailures + 1}/{TicketMaxFailures})");
                    var json = await SsoBridge.AcquireJsonAsync(_config.Auth.Ticket);
                    var action = BootstrapAction.FromJson(json);
                    ApplyBootstrap(action);
                    return; // 后续导航结果在 OnNavigationCompleted 判定
                }
                catch (Exception ex)
                {
                    _ticketFailures++;
                    Logger.Error($"ticket sso failed ({_ticketFailures}/{TicketMaxFailures}): {ex.Message}");
                    if (TicketFallenBack) break;
                    await Task.Delay(Math.Min(10000, 2000 * _ticketFailures));
                }
            }
            Logger.Error("ticket sso fallen back to simulated login");
            NotifyTicketFallback();
            NavigateHome();
        }
        finally
        {
            _ticketRunning = false;
        }
    }

    /// <summary>降级告警（全程只报一次）。</summary>
    private void NotifyTicketFallback()
    {
        if (_ticketFallbackNotified) return;
        _ticketFallbackNotified = true;
        _ = WebhookAlerter.PostAsync(_config.Watchdog.WebhookUrl,
            "sso-fallback", $"票据 SSO 连续失败 {TicketMaxFailures} 次，已降级模拟登录");
    }

    /// <summary>在 WebView2 上执行四种会话引导动作之一。</summary>
    private void ApplyBootstrap(BootstrapAction action)
    {
        var cw = _web.CoreWebView2;
        Logger.Info($"sso bootstrap: {action.Kind} {action.Url}");
        switch (action.Kind)
        {
            case "navigateUrl":
                cw.Navigate(action.Url);
                break;
            case "headers":
            {
                var headers = string.Join("\r\n", action.Headers.Select(kv => $"{kv.Key}: {kv.Value}"));
                var req = cw.Environment.CreateWebResourceRequest(action.Url, "GET", null, headers);
                cw.NavigateWithWebResourceRequest(req);
                break;
            }
            case "postForm":
            {
                var body = string.Join("&", action.Fields.Select(kv =>
                    $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
                var stream = new MemoryStream(Encoding.UTF8.GetBytes(body));
                var req = cw.Environment.CreateWebResourceRequest(action.Url, "POST", stream,
                    "Content-Type: application/x-www-form-urlencoded");
                cw.NavigateWithWebResourceRequest(req);
                break;
            }
            case "cookies":
            {
                var cm = cw.CookieManager;
                foreach (var c in action.Cookies)
                {
                    var cookie = cm.CreateCookie(c.Name, c.Value, c.Domain,
                        string.IsNullOrEmpty(c.Path) ? "/" : c.Path);
                    cm.AddOrUpdateCookie(cookie);
                }
                if (!string.IsNullOrEmpty(action.Url)) cw.Navigate(action.Url);
                break;
            }
        }
    }

    // ---------------- 模拟登录 ----------------

    private async void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess)
        {
            Logger.Warning($"navigation failed: {e.WebErrorStatus}");
            return;
        }

        // 票据 SSO：成功判定 + 会话失效重取
        if (TicketMode && !_settingsOpen)
        {
            var url = _web.Source?.ToString() ?? "";
            var pat = _config.Auth.Ticket.ReAuthWhenUrlContains;
            if (!string.IsNullOrEmpty(pat) && url.Contains(pat, StringComparison.OrdinalIgnoreCase))
            {
                // 落到登录页：取票失败或会话失效 → 记一次失败并重取
                if (!_ticketRunning)
                {
                    _ticketFailures++;
                    Logger.Warning($"ticket sso landed on login page ({_ticketFailures}/{TicketMaxFailures}): {url}");
                    if (!TicketFallenBack)
                    {
                        _ = RunTicketAuthAsync("reauth");
                        return;
                    }
                    Logger.Error("ticket sso fallen back to simulated login");
                    NotifyTicketFallback();
                }
            }
            else if (_ticketFailures > 0 && !_ticketRunning)
            {
                _ticketFailures = 0; // 到达非登录页，视为 SSO 成功
                Logger.Info("ticket sso success");
            }
            if (!TicketFallenBack) return; // 未降级前不走模拟登录
        }

        if (_settingsOpen || !_config.AutoLogin.Enabled || _loginFailures >= 3) return;

        try
        {
            var user = CredentialCrypto.Decrypt(_config.AutoLogin.Credentials.Username);
            var pass = CredentialCrypto.Decrypt(_config.AutoLogin.Credentials.Password);
            if (string.IsNullOrEmpty(user)) return;

            var login = new AutoLogin(_web, _config.AutoLogin);
            var result = await login.RunAsync(user, pass);
            switch (result)
            {
                case "success":
                    _loginFailures = 0;
                    Logger.Info("auto login success");
                    break;
                case "failed":
                    _loginFailures++;
                    Logger.Warning($"auto login failed ({_loginFailures}/3)");
                    if (_loginFailures >= 3)
                    {
                        Logger.Error("auto login paused after 3 consecutive failures");
                        _ = WebhookAlerter.PostAsync(_config.Watchdog.WebhookUrl,
                            "login-locked", "模拟登录连续失败 3 次，已暂停自动登录");
                    }
                    break;
                case "notfound":
                    break; // 非登录页，正常情况
                default:
                    Logger.Warning($"auto login result: {result}");
                    break;
            }
        }
        catch (Exception ex)
        {
            Logger.Error("auto login error", ex);
        }
    }

    // ---------------- 软键盘 ----------------

    private void SetKeyboardVisible(bool visible)
    {
        if (visible)
        {
            _tabTip.KillNow();
            // 按当前窗口高度百分比计算键盘高度
            var h = (int)(ClientSize.Height * (_config.Keyboard.HeightPercent / 100.0));
            if (h > 0 && _keyboardHost.Height != h) _keyboardHost.Height = h;
        }
        if (_keyboardHost.Visible != visible)
        {
            _keyboardHost.Visible = visible;
            Logger.Debug($"keyboard {(visible ? "shown" : "hidden")}");
        }
        if (!visible)
        {
            _keyboard.ClearComposition();
            _candBar.Visible = false;
        }
        // 设置页打开期间：键盘显隐时遮罩同步伸缩
        if (_settingsMask != null)
            _settingsMask.Height = visible && _config.Keyboard.Enabled
                ? _keyboardHost.Top
                : ClientSize.Height;
    }

    /// <summary>递归给容器内所有 TextBox 挂焦点弹键盘（设置页用）。</summary>
    private void AttachKeyboardOnFocus(Control root)
    {
        foreach (Control c in root.Controls)
        {
            if (c is TextBox)
                c.GotFocus += (s, e) =>
                {
                    if (_settingsOpen && _config.Keyboard.Enabled) SetKeyboardVisible(true);
                };
            else if (c.HasChildren)
                AttachKeyboardOnFocus(c);
        }
    }

    /// <summary>候选栏悬浮在键盘正上方（不挤压键盘），随组合串显隐。</summary>
    private void UpdateCandidateBar()
    {
        if (_keyboard.Composition.Length == 0 || !_keyboardHost.Visible)
        {
            _candBar.Visible = false;
            return;
        }
        _candBar.SetContent(_keyboard.Composition, _keyboard.Candidates);
        ShowCandBarAboveKeyboard();
    }

    private void ShowCandBarAboveKeyboard()
    {
        _candBar.SetBounds(0, _keyboardHost.Top - _candBar.Height, Width, _candBar.Height);
        _candBar.Visible = true;
        _candBar.BringToFront();
    }

    // ---------------- 语音输入 ----------------

    private void OnVoiceStart(object? sender, EventArgs e)
    {
        if (_voice == null)
        {
            ShowCandBarAboveKeyboard();
            _candBar.SetStatus(_config.Speech.Enabled
                ? "语音模型加载中/未就绪，请稍候…"
                : "语音未启用：请在设置中开启");
            return;
        }
        ShowCandBarAboveKeyboard();
        _candBar.SetStatus("正在聆听…");
        _voice.Begin();
    }

    private void OnVoiceStop(object? sender, EventArgs e) => _voice?.End();

    private async void OnKeyboardKey(object? sender, KioskKeyEventArgs e)
    {
        // 设置页打开时：按键路由到 WinForms 焦点输入框（触屏输入设置项）
        if (_settingsOpen)
        {
            SendKeyToWinForms(e);
            return;
        }
        if (!_webReady) return;
        var script = e.Special switch
        {
            SpecialKey.Backspace => InjectedScripts.Backspace,
            SpecialKey.Enter => InjectedScripts.Enter,
            SpecialKey.Tab => InjectedScripts.FocusNext,
            _ => InjectedScripts.InsertText(e.Text ?? "")
        };
        try { await _web.CoreWebView2.ExecuteScriptAsync(script); }
        catch (Exception ex) { Logger.Warning($"key inject failed: {ex.Message}"); }
    }

    /// <summary>把软键盘输入写入当前获得焦点的 WinForms TextBox。</summary>
    private void SendKeyToWinForms(KioskKeyEventArgs e)
    {
        var tb = FindFocusedTextBox(this);
        if (tb == null) return;
        switch (e.Special)
        {
            case SpecialKey.Backspace:
                if (tb.SelectionLength > 0) tb.SelectedText = "";
                else if (tb.SelectionStart > 0)
                {
                    var s = tb.SelectionStart;
                    tb.Text = tb.Text.Remove(s - 1, 1);
                    tb.SelectionStart = s - 1;
                }
                break;
            case SpecialKey.Enter:
            case SpecialKey.Tab:
                SelectNextControl(tb, true, true, true, true);
                break;
            default:
                if (e.Text != null) tb.SelectedText = e.Text;
                break;
        }
    }

    private static TextBox? FindFocusedTextBox(Control root)
    {
        foreach (Control c in root.Controls)
        {
            if (c is TextBox tb && tb.Focused) return tb;
            var found = FindFocusedTextBox(c);
            if (found != null) return found;
        }
        return null;
    }

    // ---------------- 设置面板 ----------------

    private void OpenSettings()
    {
        if (_settingsOpen) return;

        // 配置了入口密码 → 先过密码门（密码门自带软键盘）
        var hash = _config.SettingsEntry.PasswordHash;
        if (!string.IsNullOrEmpty(hash))
        {
            var gate = new PasswordGate(hash);
            Controls.Add(gate);
            gate.BringToFront();
            gate.Passed += (s, e) =>
            {
                Controls.Remove(gate);
                gate.Dispose();
                OpenSettingsPanel();
            };
            gate.Cancelled += (s, e) =>
            {
                Controls.Remove(gate);
                gate.Dispose();
            };
            return;
        }
        OpenSettingsPanel();
    }

    private void OpenSettingsPanel()
    {
        _settingsOpen = true;
        Logger.Info("settings panel opened");

        // 设置面板是 WinForms 控件，TabTip 已被杀，必须强制弹出自带软键盘
        if (_config.Keyboard.Enabled) SetKeyboardVisible(true);

        _settingsMask = new Panel
        {
            Dock = DockStyle.Top,
            Height = _config.Keyboard.Enabled ? _keyboardHost.Top : Height,
            BackColor = Color.FromArgb(18, 18, 22)
        };
        _settings = new SettingsPanel(_config.Clone());
        _settings.SaveRestartRequested += OnSaveRestart;
        _settings.SaveOnlyRequested += OnSaveOnly;
        _settings.CancelRequested += (s, e) => CloseSettings();
        _settings.ExitRequested += OnExitRequested;

        _settingsMask.Controls.Add(_settings);
        Controls.Add(_settingsMask);
        _settingsMask.BringToFront();

        // 设置页内输入框获得焦点时重新弹出键盘
        AttachKeyboardOnFocus(_settingsMask);

        // 面板尺寸必须在 mask 加入布局后再计算（之前读的是未布局默认宽度，导致面板被压成窄条）
        void LayoutSettings()
        {
            _settings.Size = new Size(
                Math.Min(DpiHelper.S(960), Math.Max(200, _settingsMask.Width - DpiHelper.S(40))),
                Math.Min(DpiHelper.S(800), Math.Max(150, _settingsMask.Height - DpiHelper.S(40))));
            _settings.Location = new Point(
                (_settingsMask.Width - _settings.Width) / 2,
                (_settingsMask.Height - _settings.Height) / 2);
        }
        LayoutSettings();
        _settingsMask.Resize += (s, e) => LayoutSettings();
    }

    private void CloseSettings()
    {
        if (_settingsMask != null)
        {
            Controls.Remove(_settingsMask);
            _settingsMask.Dispose();
            _settingsMask = null;
            _settings = null;
        }
        _settingsOpen = false;
        SetKeyboardVisible(false);
    }

    private void OnSaveRestart(object? sender, EventArgs e)
    {
        if (_settings == null) return;
        ConfigStore.Save(CurrentPanelConfig());
        Logger.Info("config saved, restarting");
        RestartApp();
    }

    private void OnSaveOnly(object? sender, EventArgs e)
    {
        if (_settings == null) return;
        ConfigStore.Save(CurrentPanelConfig());
        _settings.ShowMessage("已保存（部分配置需重启后生效）");
    }

    private KioskConfig CurrentPanelConfig()
    {
        // SettingsPanel.ApplyToConfig 已在触发事件前调用并写回其内部副本；
        // 这里需要拿到那份副本——通过重新序列化面板的配置。
        return _settings!.ExportConfig();
    }

    private void OnExitRequested(object? sender, EventArgs e)    {
        if (!ConfirmDialog.Show(this, "确定要退出 Kiosk 模式吗？\n守护进程将被一并停止。", "退出 Kiosk"))
            return;
        Logger.Info("admin exit requested");
        KillWatchdog();
        _ = _heartbeat.SendByeAsync();
        _allowClose = true;
        Application.Exit();
    }

    private static void KillWatchdog()
    {
        foreach (var p in Process.GetProcessesByName("KioskWatchdog"))
        {
            try { p.Kill(); Logger.Info("watchdog stopped"); } catch { /* ignore */ }
            p.Dispose();
        }
    }

    private void RestartApp()
    {
        _allowClose = true;
        try
        {
            var exePath = Application.ExecutablePath;
            ProcessStartInfo psi;
            if (Path.GetFileNameWithoutExtension(exePath).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            {
                // 通过 dotnet 宿主启动时（如调试/测试），需要用 dll 路径重启
                var dll = System.Reflection.Assembly.GetExecutingAssembly().Location;
                psi = new ProcessStartInfo(exePath, $"\"{dll}\"") { UseShellExecute = true };
            }
            else
            {
                psi = new ProcessStartInfo(exePath) { UseShellExecute = true };
            }
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            Logger.Error("restart failed", ex);
        }
        Application.Exit();
    }

    // ---------------- 专业版插件（远程管理） ----------------

    private void StartPro()
    {
        if (!_config.Remote.Enabled || string.IsNullOrWhiteSpace(_config.Remote.ServerUrl))
        {
            Logger.Info("community edition (remote disabled)");
            return;
        }
        _pro = ProBridge.TryLoad();
        if (_pro == null)
        {
            Logger.Warning("KioskBrowser.Pro.dll 未安装：远程管理不可用（社区版）");
            return;
        }
        try
        {
            _pro.Start(this);
            Logger.Info($"pro plugin started: {_pro.Name}");
        }
        catch (Exception ex)
        {
            Logger.Error("pro plugin start failed", ex);
            _pro = null;
        }
    }

    // ---------------- IProHost（暴露给专业版插件的宿主能力） ----------------

    KioskConfig IProHost.Config => _config;
    void IProHost.SaveConfig() => ConfigStore.Save(_config);

    void IProHost.Navigate(string url)
    {
        if (_webReady) _web.CoreWebView2.Navigate(url);
    }

    void IProHost.RestartApp() => RestartApp();

    void IProHost.ShutdownAll()
    {
        KillWatchdog();
        _ = _heartbeat.SendByeAsync();
        _allowClose = true;
        Application.Exit();
    }

    Task<string> IProHost.CaptureScreenshotBase64Async() => CaptureScreenshotBase64Async();

    void IProHost.ReconfigureVocabulary(List<string> vocabularies, string customVocabulary)
    {
        _config.Keyboard.Vocabularies = vocabularies;
        _config.Keyboard.CustomVocabulary = customVocabulary;
        ConfigStore.Save(_config);
        Task.Run(() =>
        {
            PinyinEngine.Instance.Configure(vocabularies, customVocabulary);
            HotwordManager.WriteHotwordsFile(PinyinEngine.Instance.BoostWords);
        });
    }

    void IProHost.SetSettingsPassword(string password)
    {
        _config.SettingsEntry.PasswordHash =
            password.Length > 0 ? PasswordHasher.Hash(password) : "";
        ConfigStore.Save(_config);
    }

    void IProHost.RunOnUi(Action action) => BeginInvoke(action);

    void IProHost.ShowSettingsMessage(string msg) => _settings?.ShowMessage(msg);

    /// <summary>截屏：GDI 截整窗（WinForms 覆盖层：设置面板/软键盘/广告条）
    /// + WebView2 CapturePreview 补页面内容（DirectComposition GDI 截不到）。</summary>
    private async Task<string> CaptureScreenshotBase64Async()
    {
        using var bmp = new Bitmap(Width, Height);
        using (var g = Graphics.FromImage(bmp))
            g.CopyFromScreen(PointToScreen(new Point(0, 0)), Point.Empty, new Size(Width, Height));

        if (_webReady)
        {
            try
            {
                using var ms = new MemoryStream();
                await _web.CoreWebView2.CapturePreviewAsync(
                    CoreWebView2CapturePreviewImageFormat.Jpeg, ms);
                ms.Position = 0;
                using var webImg = Image.FromStream(ms);
                using var g2 = Graphics.FromImage(bmp);
                g2.DrawImage(webImg, new Rectangle(_web.Left, _web.Top, _web.Width, _web.Height));
            }
            catch (Exception ex)
            {
                Logger.Warning($"webview capture failed: {ex.Message}");
            }
        }
        using var msOut = new MemoryStream();
        bmp.Save(msOut, ImageFormat.Jpeg);
        return Convert.ToBase64String(msOut.ToArray());
    }

    // ---------------- 错误展示 ----------------

    private void ShowFatalError(string message)
    {
        var lbl = new Label
        {
            Text = message,
            Dock = DockStyle.Fill,
            ForeColor = Color.White,
            BackColor = Color.FromArgb(30, 20, 20),
            Font = new Font("Microsoft YaHei UI", 12f),
            TextAlign = ContentAlignment.MiddleCenter
        };
        Controls.Add(lbl);
        lbl.BringToFront();
    }

    private void ShowErrorPage(string title, string htmlBody)
    {
        if (!_webReady) return;
        var html = @"<!DOCTYPE html><html><head><meta charset=""utf-8""><style>
body{background:#1a1a20;color:#eee;font-family:'Microsoft YaHei',sans-serif;
     display:flex;flex-direction:column;align-items:center;justify-content:center;
     height:100vh;margin:0;}
h1{color:#e08080;} p{color:#aaa;max-width:70%;text-align:center;}
</style></head><body><h1>__TITLE__</h1><p>__BODY__</p></body></html>"
            .Replace("__TITLE__", title)
            .Replace("__BODY__", htmlBody);
        _web.CoreWebView2.NavigateToString(html);
    }

    // ---------------- 关闭保护 ----------------

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (!_allowClose && _config.Browser.KioskMode)
        {
            e.Cancel = true; // 霸屏模式拦截 WM_CLOSE；普通窗口模式可直接关闭
            return;
        }
        _hook.Dispose();
        _tabTip.Dispose();
        _heartbeatTimer.Stop();
        _heartbeat.Dispose();
        _voice?.Dispose();
    }
}
