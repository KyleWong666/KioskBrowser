using KioskBrowser.Shared;

namespace KioskBrowser;

/// <summary>
/// 设置面板：模态覆盖在 Kiosk 页面上方。
/// 编辑的是配置副本，保存时才回写；取消则丢弃。
/// </summary>
public sealed class SettingsPanel : UserControl
{
    public event EventHandler? SaveRestartRequested;
    public event EventHandler? SaveOnlyRequested;
    public event EventHandler? CancelRequested;
    public event EventHandler? ExitRequested;

    private readonly KioskConfig _cfg;

    private TextBox _txtUrl = null!;
    private TextBox _txtWhitelist = null!;
    private CheckBox _chkKiosk = null!;
    private TextBox _txtEntryPassword = null!;
    private CheckBox _chkClearPassword = null!;
    private CheckBox _chkLogin = null!;
    private TextBox _txtUser = null!;
    private TextBox _txtPass = null!;
    private TextBox _txtUserSel = null!;
    private TextBox _txtPassSel = null!;
    private TextBox _txtSubmitSel = null!;
    private NumericUpDown _numZone = null!;
    private NumericUpDown _numTaps = null!;
    private NumericUpDown _numKbHeight = null!;
    private TextBox _txtWebhook = null!;
    private CheckBox _chkSpeech = null!;
    private ComboBox _cmbModelSize = null!;
    private Label _lblModelStatus = null!;
    private CheckBox _chkRemote = null!;
    private TextBox _txtServerUrl = null!;
    private Label _lblActivation = null!;
    private CheckBox _chkVocabGeneral = null!;

    /// <summary>点击「扫码激活」。</summary>
    public event EventHandler? ActivateRequested;
    private CheckBox _chkVocabGov = null!;
    private CheckBox _chkVocabMedical = null!;
    private CheckBox _chkVocabRetail = null!;
    private CheckBox _chkVocabIndustrial = null!;
    private TextBox _txtCustomVocab = null!;
    private Label _lblMsg = null!;

    private static readonly Color BgColor = Color.FromArgb(28, 28, 34);
    private static readonly Color InputColor = Color.FromArgb(45, 45, 54);
    private static readonly Color FgColor = Color.FromArgb(230, 230, 235);
    private static readonly Font LabelFont = new("Microsoft YaHei UI", 10f);
    private static readonly Font InputFont = new("Microsoft YaHei UI", 10.5f);

    public SettingsPanel(KioskConfig cfg)
    {
        _cfg = cfg;
        BackColor = BgColor;
        BuildUi();
        Bind();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(DpiHelper.S(24), DpiHelper.S(16), DpiHelper.S(24), DpiHelper.S(16)),
            BackColor = BgColor
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, DpiHelper.S(48)));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, DpiHelper.S(120)));

        var title = new Label
        {
            Text = "Kiosk 设置",
            Dock = DockStyle.Fill,
            ForeColor = Color.White,
            Font = new Font("Microsoft YaHei UI", 14f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        };
        root.Controls.Add(title, 0, 0);

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            AutoScroll = true,
            BackColor = BgColor
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, DpiHelper.S(170)));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _txtUrl = AddRow(grid, "目标 URL");
        _txtWhitelist = AddRow(grid, "导航白名单(逗号分隔,空=不限)");

        _chkKiosk = new CheckBox
        {
            Text = "霸屏模式（全屏锁定，取消勾选便于测试，重启生效）",
            Dock = DockStyle.Fill,
            ForeColor = FgColor,
            Font = LabelFont
        };
        AddRowCustom(grid, "霸屏", _chkKiosk);
        _txtEntryPassword = AddRow(grid, "设置入口新密码(留空不变)");
        _txtEntryPassword.PasswordChar = '●';
        _chkClearPassword = new CheckBox
        {
            Text = "清除设置入口密码",
            Dock = DockStyle.Fill,
            ForeColor = FgColor,
            Font = LabelFont
        };
        AddRowCustom(grid, "入口密码", _chkClearPassword);

        _chkLogin = new CheckBox
        {
            Text = "启用模拟登录",
            Dock = DockStyle.Fill,
            ForeColor = FgColor,
            Font = LabelFont
        };
        AddRowCustom(grid, "模拟登录", _chkLogin);
        _txtUser = AddRow(grid, "登录用户名");
        _txtPass = AddRow(grid, "登录密码");
        _txtPass.PasswordChar = '●';
        _txtUserSel = AddRow(grid, "用户名选择器");
        _txtPassSel = AddRow(grid, "密码选择器");
        _txtSubmitSel = AddRow(grid, "提交按钮选择器");
        _numZone = AddNumRow(grid, "触发区边长(px)", 20, 400);
        _numTaps = AddNumRow(grid, "连击次数", 3, 20);
        _numKbHeight = AddNumRow(grid, "键盘高度(%)", 20, 60);
        _txtWebhook = AddRow(grid, "告警 Webhook(可空)");

        // 远程管理（付费版）
        _chkRemote = new CheckBox
        {
            Text = "启用远程管理(付费版功能)",
            Dock = DockStyle.Fill,
            ForeColor = FgColor,
            Font = LabelFont
        };
        AddRowCustom(grid, "远程管理", _chkRemote);
        _txtServerUrl = AddRow(grid, "管理后台 URL");

        // 付费激活状态 + 扫码按钮
        var actPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = BgColor,
            Margin = new Padding(0, 4, 0, 4)
        };
        _lblActivation = new Label
        {
            AutoSize = true,
            ForeColor = FgColor,
            Font = LabelFont,
            Margin = new Padding(0, 10, 20, 0)
        };
        var btnActivate = new Button
        {
            Text = "扫码激活",
            Width = DpiHelper.S(130),
            Height = DpiHelper.S(34),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(50, 110, 200),
            ForeColor = Color.White,
            Font = LabelFont
        };
        btnActivate.Click += (s, e) => ActivateRequested?.Invoke(this, EventArgs.Empty);
        actPanel.Controls.Add(_lblActivation);
        actPanel.Controls.Add(btnActivate);
        AddRowCustom(grid, "付费激活", actPanel);

        // 语音输入（本地 sherpa-onnx，离线）
        _chkSpeech = new CheckBox
        {
            Text = "启用语音输入(本地离线识别)",
            Dock = DockStyle.Fill,
            ForeColor = FgColor,
            Font = LabelFont
        };
        AddRowCustom(grid, "语音输入", _chkSpeech);

        _cmbModelSize = new ComboBox
        {
            Dock = DockStyle.Left,
            Width = DpiHelper.S(340),
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = InputColor,
            ForeColor = FgColor,
            Font = InputFont
        };
        foreach (var m in Voice.ModelManager.Models) _cmbModelSize.Items.Add(m.DisplayName);
        AddRowCustom(grid, "语音模型档位", _cmbModelSize);

        _lblModelStatus = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(150, 150, 160),
            Font = LabelFont,
            TextAlign = ContentAlignment.MiddleLeft
        };
        AddRowCustom(grid, "模型状态", _lblModelStatus);

        var lblHotwordNote = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(150, 150, 160),
            Font = LabelFont,
            TextAlign = ContentAlignment.MiddleLeft,
            Text = "热词 = 勾选的专业词库(政务/医疗/商场/工业) + 自定义词库；" +
                   "仅「中文2025」支持热词，大模型不支持词库。"
        };
        AddRowCustom(grid, "热词说明", lblHotwordNote);

        // 词库多选
        var vocabPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = BgColor,
            Margin = new Padding(0, 4, 0, 4)
        };
        (_chkVocabGeneral, _chkVocabGov, _chkVocabMedical, _chkVocabRetail, _chkVocabIndustrial) = (
            MakeVocabCheck("通用词库(18万词)"), MakeVocabCheck("政务服务"),
            MakeVocabCheck("医疗健康"), MakeVocabCheck("商场零售"), MakeVocabCheck("工业工控"));
        vocabPanel.Controls.AddRange(new Control[]
            { _chkVocabGeneral, _chkVocabGov, _chkVocabMedical, _chkVocabRetail, _chkVocabIndustrial });
        AddRowCustom(grid, "拼音词库(多选)", vocabPanel);

        // 自定义词库（多行）
        _txtCustomVocab = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = InputColor,
            ForeColor = FgColor,
            Font = InputFont,
            BorderStyle = BorderStyle.FixedSingle
        };
        var customRow = grid.RowCount++;
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, DpiHelper.S(96)));
        // 左列：标题 + 导入按钮
        var customLabelPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            BackColor = BgColor,
            WrapContents = false
        };
        customLabelPanel.Controls.Add(new Label
        {
            Text = "自定义词库",
            AutoSize = true,
            ForeColor = FgColor,
            Font = LabelFont
        });
        customLabelPanel.Controls.Add(new Label
        {
            Text = "(空格/逗号分隔)",
            AutoSize = true,
            ForeColor = Color.FromArgb(140, 145, 165),
            Font = new Font(LabelFont.FontFamily, LabelFont.Size - 1.5f)
        });
        var btnImport = new Button
        {
            Text = "导入文本…",
            AutoSize = true,
            BackColor = Color.FromArgb(70, 70, 82),
            ForeColor = FgColor,
            FlatStyle = FlatStyle.Flat,
            Font = LabelFont,
            Margin = new Padding(0, 6, 0, 0)
        };
        btnImport.Click += (s, e) => ImportVocabulary();
        customLabelPanel.Controls.Add(btnImport);
        grid.Controls.Add(customLabelPanel, 0, customRow);
        _txtCustomVocab.Margin = new Padding(0, 4, 0, 4);
        grid.Controls.Add(_txtCustomVocab, 1, customRow);

        root.Controls.Add(grid, 0, 1);

        // 底部：消息 + 按钮两行
        var bottom = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = BgColor
        };
        bottom.RowStyles.Add(new RowStyle(SizeType.Absolute, DpiHelper.S(28)));
        bottom.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _lblMsg = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(255, 180, 80),
            Font = LabelFont,
            TextAlign = ContentAlignment.MiddleLeft
        };
        bottom.Controls.Add(_lblMsg, 0, 0);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = BgColor
        };
        buttons.Controls.Add(MakeButton("保存并重启", Color.FromArgb(50, 110, 200),
            () => { if (ApplyToConfig()) SaveRestartRequested?.Invoke(this, EventArgs.Empty); }));
        buttons.Controls.Add(MakeButton("仅保存", Color.FromArgb(60, 130, 90),
            () => { if (ApplyToConfig()) SaveOnlyRequested?.Invoke(this, EventArgs.Empty); }));
        buttons.Controls.Add(MakeButton("取消", Color.FromArgb(70, 70, 82),
            () => CancelRequested?.Invoke(this, EventArgs.Empty)));
        buttons.Controls.Add(MakeButton("恢复默认", Color.FromArgb(150, 110, 40), RestoreDefaults));
        buttons.Controls.Add(MakeButton("退出 Kiosk", Color.FromArgb(160, 60, 60),
            () => ExitRequested?.Invoke(this, EventArgs.Empty)));
        bottom.Controls.Add(buttons, 0, 1);

        root.Controls.Add(bottom, 0, 2);
        Controls.Add(root);
    }

    private TextBox AddRow(TableLayoutPanel grid, string label)
    {
        var tb = new TextBox
        {
            Dock = DockStyle.Fill,
            BackColor = InputColor,
            ForeColor = FgColor,
            Font = InputFont,
            BorderStyle = BorderStyle.FixedSingle
        };
        AddRowCustom(grid, label, tb);
        return tb;
    }

    private NumericUpDown AddNumRow(TableLayoutPanel grid, string label, int min, int max)
    {
        var num = new NumericUpDown
        {
            Dock = DockStyle.Left,
            Width = DpiHelper.S(120),
            Minimum = min,
            Maximum = max,
            BackColor = InputColor,
            ForeColor = FgColor,
            Font = InputFont
        };
        AddRowCustom(grid, label, num);
        return num;
    }

    private void AddRowCustom(TableLayoutPanel grid, string label, Control control)
    {
        var row = grid.RowCount++;
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, DpiHelper.S(38)));
        grid.Controls.Add(new Label
        {
            Text = label,
            Dock = DockStyle.Fill,
            ForeColor = FgColor,
            Font = LabelFont,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, row);
        control.Margin = new Padding(0, 4, 0, 4);
        grid.Controls.Add(control, 1, row);
    }

    private void UpdateModelStatus()
    {
        if (_cmbModelSize.SelectedIndex < 0) return;
        var key = Voice.ModelManager.Models[_cmbModelSize.SelectedIndex].Key;
        var ready = Voice.ModelManager.IsReady(key);
        _lblModelStatus.Text = ready
            ? "已就绪"
            : "未下载（保存并重启后自动下载，需联网一次）";
        _lblModelStatus.ForeColor = ready
            ? Color.FromArgb(110, 220, 140)
            : Color.FromArgb(255, 180, 80);
    }

    private CheckBox MakeVocabCheck(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = FgColor,
        Font = LabelFont,
        Margin = new Padding(0, 6, 18, 0)
    };

    private Button MakeButton(string text, Color bg, Action onClick)
    {
        var btn = new Button
        {
            Text = text,
            Width = DpiHelper.S(124),
            Height = DpiHelper.S(48),
            Margin = new Padding(0, 4, 14, 4),
            FlatStyle = FlatStyle.Flat,
            BackColor = bg,
            ForeColor = Color.White,
            Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold)
        };
        btn.Click += (s, e) => onClick();
        return btn;
    }

    private void Bind()
    {
        _txtUrl.Text = _cfg.Browser.HomeUrl;
        _txtWhitelist.Text = string.Join(",", _cfg.Browser.NavigationWhitelist);
        _chkKiosk.Checked = _cfg.Browser.KioskMode;
        _chkClearPassword.Checked = false;
        _chkClearPassword.Text = string.IsNullOrEmpty(_cfg.SettingsEntry.PasswordHash)
            ? "清除设置入口密码（当前未设置）"
            : "清除设置入口密码（当前已设置）";
        _chkLogin.Checked = _cfg.AutoLogin.Enabled;
        _txtUser.Text = CredentialCrypto.Decrypt(_cfg.AutoLogin.Credentials.Username);
        _txtPass.Text = CredentialCrypto.Decrypt(_cfg.AutoLogin.Credentials.Password);
        _txtUserSel.Text = _cfg.AutoLogin.UsernameSelector;
        _txtPassSel.Text = _cfg.AutoLogin.PasswordSelector;
        _txtSubmitSel.Text = _cfg.AutoLogin.SubmitSelector;
        _numZone.Value = Math.Clamp(_cfg.SettingsEntry.ZoneSize, (int)_numZone.Minimum, (int)_numZone.Maximum);
        _numTaps.Value = Math.Clamp(_cfg.SettingsEntry.TapCount, (int)_numTaps.Minimum, (int)_numTaps.Maximum);
        _numKbHeight.Value = Math.Clamp(_cfg.Keyboard.HeightPercent, (int)_numKbHeight.Minimum, (int)_numKbHeight.Maximum);
        _txtWebhook.Text = _cfg.Watchdog.WebhookUrl;
        _chkRemote.Checked = _cfg.Remote.Enabled;
        _txtServerUrl.Text = _cfg.Remote.ServerUrl;
        SetActivationText("未激活（单机版免费使用）");
        _chkSpeech.Checked = _cfg.Speech.Enabled;
        var modelIdx = Array.FindIndex(Voice.ModelManager.Models, m => m.Key == _cfg.Speech.ModelSize);
        _cmbModelSize.SelectedIndex = modelIdx >= 0 ? modelIdx : 0; // 默认中文2025
        UpdateModelStatus();
        _cmbModelSize.SelectedIndexChanged += (s, e) => UpdateModelStatus();
        _chkVocabGeneral.Checked = _cfg.Keyboard.Vocabularies.Contains("general");
        _chkVocabGov.Checked = _cfg.Keyboard.Vocabularies.Contains("gov");
        _chkVocabMedical.Checked = _cfg.Keyboard.Vocabularies.Contains("medical");
        _chkVocabRetail.Checked = _cfg.Keyboard.Vocabularies.Contains("retail");
        _chkVocabIndustrial.Checked = _cfg.Keyboard.Vocabularies.Contains("industrial");
        _txtCustomVocab.Text = _cfg.Keyboard.CustomVocabulary;
    }

    /// <summary>校验并把界面值写回配置副本。失败时显示错误并返回 false。</summary>
    public bool ApplyToConfig()
    {
        var url = _txtUrl.Text.Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            _lblMsg.Text = "目标 URL 无效，请输入 http:// 或 https:// 开头的完整地址";
            return false;
        }
        _cfg.Browser.HomeUrl = url;
        _cfg.Browser.NavigationWhitelist = _txtWhitelist.Text
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        _cfg.Browser.KioskMode = _chkKiosk.Checked;

        // 设置入口密码：清除优先；其次非空则更新
        if (_chkClearPassword.Checked)
            _cfg.SettingsEntry.PasswordHash = "";
        else if (_txtEntryPassword.Text.Length > 0)
            _cfg.SettingsEntry.PasswordHash = PasswordHasher.Hash(_txtEntryPassword.Text);
        _cfg.AutoLogin.Enabled = _chkLogin.Checked;
        _cfg.AutoLogin.Credentials.Username = CredentialCrypto.Encrypt(_txtUser.Text);
        _cfg.AutoLogin.Credentials.Password = CredentialCrypto.Encrypt(_txtPass.Text);
        _cfg.AutoLogin.UsernameSelector = _txtUserSel.Text.Trim();
        _cfg.AutoLogin.PasswordSelector = _txtPassSel.Text.Trim();
        _cfg.AutoLogin.SubmitSelector = _txtSubmitSel.Text.Trim();
        _cfg.SettingsEntry.ZoneSize = (int)_numZone.Value;
        _cfg.SettingsEntry.TapCount = (int)_numTaps.Value;
        _cfg.Keyboard.HeightPercent = (int)_numKbHeight.Value;
        _cfg.Watchdog.WebhookUrl = _txtWebhook.Text.Trim();
        _cfg.Remote.Enabled = _chkRemote.Checked;
        _cfg.Remote.ServerUrl = _txtServerUrl.Text.Trim();
        if (_cfg.Remote.Enabled && string.IsNullOrEmpty(_cfg.Remote.ServerUrl))
        {
            _lblMsg.Text = "启用远程管理需要填写管理后台 URL";
            return false;
        }
        _cfg.Speech.Enabled = _chkSpeech.Checked;
        if (_cmbModelSize.SelectedIndex >= 0)
            _cfg.Speech.ModelSize = Voice.ModelManager.Models[_cmbModelSize.SelectedIndex].Key;

        // 词库：多选 + 自定义，不能为空
        var vocabs = new List<string>();
        if (_chkVocabGeneral.Checked) vocabs.Add("general");
        if (_chkVocabGov.Checked) vocabs.Add("gov");
        if (_chkVocabMedical.Checked) vocabs.Add("medical");
        if (_chkVocabRetail.Checked) vocabs.Add("retail");
        if (_chkVocabIndustrial.Checked) vocabs.Add("industrial");
        var custom = _txtCustomVocab.Text.Trim();
        if (vocabs.Count == 0 && custom.Length == 0)
        {
            _lblMsg.Text = "词库不能为空：请至少勾选一个词库，或填写自定义词库";
            return false;
        }
        _cfg.Keyboard.Vocabularies = vocabs;
        _cfg.Keyboard.CustomVocabulary = custom;
        _lblMsg.Text = "";
        return true;
    }

    /// <summary>词库分词：空格/换行/逗号/顿号/分号均可分隔（与引擎一致）。</summary>
    private static readonly System.Text.RegularExpressions.Regex VocabSplitter =
        new(@"[\s,，、;；]+", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>从文本文件导入自定义词库：读取 → 与现有内容合并去重 → 回填编辑框。</summary>
    private void ImportVocabulary()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "导入自定义词库",
            Filter = "文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dlg.ShowDialog() != DialogResult.OK) return;

        string content;
        try
        {
            var bytes = File.ReadAllBytes(dlg.FileName);
            // 中文词表常见 UTF-8 / GBK 两种编码：先严格 UTF-8，失败回落系统默认(GBK)
            try
            {
                content = new System.Text.UTF8Encoding(false, true).GetString(bytes);
            }
            catch (System.Text.DecoderFallbackException)
            {
                content = System.Text.Encoding.Default.GetString(bytes);
            }
        }
        catch (Exception ex)
        {
            _lblMsg.Text = "读取失败：" + ex.Message;
            return;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var merged = new List<string>();
        foreach (var w in VocabSplitter.Split(_txtCustomVocab.Text))
            if (w.Trim().Length > 0 && seen.Add(w.Trim())) merged.Add(w.Trim());
        var before = merged.Count;
        foreach (var w in VocabSplitter.Split(content))
            if (w.Trim().Length > 0 && seen.Add(w.Trim())) merged.Add(w.Trim());
        var added = merged.Count - before;

        // 回填：每行 8 词（便于浏览），保存时引擎按分隔符解析
        _txtCustomVocab.Text = string.Join("\n",
            merged.Select((w, i) => (w, i)).GroupBy(x => x.i / 8)
                  .Select(g => string.Join(" ", g.Select(x => x.w))));
        _lblMsg.Text = $"已导入 {added} 个新词（去重后共 {merged.Count} 个），记得保存";
    }

    private void RestoreDefaults()
    {
        if (!ConfirmDialog.Show(FindForm()!, "确定要将所有配置恢复为出厂值吗？", "恢复默认"))
            return;
        var defaults = new KioskConfig();
        _cfg.Browser = defaults.Browser;
        _cfg.SettingsEntry = defaults.SettingsEntry;
        _cfg.AutoLogin = defaults.AutoLogin;
        _cfg.Keyboard = defaults.Keyboard;
        _cfg.Watchdog = defaults.Watchdog;
        Bind();
        _lblMsg.Text = "已恢复默认值（尚未保存，点击保存按钮生效）";
    }

    public void ShowMessage(string msg) => _lblMsg.Text = msg;

    /// <summary>更新激活状态显示（已激活时显示有效期）。</summary>
    public void SetActivationText(string text)
    {
        _lblActivation.Text = text;
        _lblActivation.ForeColor = text.StartsWith("已激活")
            ? Color.FromArgb(110, 220, 140)
            : Color.FromArgb(255, 180, 80);
    }

    /// <summary>返回面板内部编辑的配置副本（ApplyToConfig 已把界面值写入）。</summary>
    public KioskConfig ExportConfig() => _cfg;
}
