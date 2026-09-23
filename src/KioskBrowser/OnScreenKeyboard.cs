namespace KioskBrowser;

public enum SpecialKey { None, Backspace, Enter, Tab, Space }

public class KioskKeyEventArgs : EventArgs
{
    public string? Text { get; init; }
    public SpecialKey Special { get; init; }
}

/// <summary>
/// 自定义软键盘：QWERTY + 符号页 + Shift + 中文拼音输入。
/// 中文模式下字母键进入组合串，候选栏点选/数字键上屏，空格选首选，回车原样上屏。
/// 按键不可获取焦点，避免 WebView 失焦收起键盘。
/// </summary>
public sealed class OnScreenKeyboard : UserControl
{
    public event EventHandler<KioskKeyEventArgs>? KeyPressed;
    public event EventHandler? HideRequested;

    /// <summary>组合串或候选列表变化时触发（宿主据此更新悬浮候选栏）。</summary>
    public event EventHandler? CompositionChanged;

    /// <summary>按住「说话」键（MouseDown）/ 松开（MouseUp）。</summary>
    public event EventHandler? VoiceStart;
    public event EventHandler? VoiceStop;

    public string Composition => _composition;
    public IReadOnlyList<string> Candidates => _candidates;

    private static readonly Color BgColor = Color.FromArgb(24, 24, 28);
    private static readonly Color KeyColor = Color.FromArgb(52, 52, 60);
    private static readonly Color SpecialColor = Color.FromArgb(38, 38, 46);
    private static readonly Color FgColor = Color.FromArgb(240, 240, 245);
    private static readonly Font KeyFont = new("Microsoft YaHei UI", 15f, FontStyle.Bold);

    private readonly bool _chineseEnabled;
    private readonly bool _speechEnabled;
    private bool _chineseMode;
    private bool _shift;
    private bool _symbols;
    private string _composition = "";
    private List<string> _candidates = new();

    private readonly List<Button> _alphaKeys = new();
    private readonly List<Button> _shiftKeys = new();
    private Button? _symbolToggle;
    private Button? _langKey;
    private Button? _voiceKey;

    // 符号模式下替换字母区的符号（与字母行键数一一对应）
    private static readonly string[] SymRowQ = { "@", "#", "$", "%", "^", "&", "*", "(", ")", "~" };
    private static readonly string[] SymRowA = { "!", "?", ":", ";", "\"", "`", "|", "_", "+" };
    private static readonly string[] SymRowZ = { "<", ">", "{", "}", "[", "]", "=" };

    // 中文模式标点映射
    private static readonly Dictionary<string, string> PunctMap = new()
    {
        [","] = "，", ["."] = "。", [";"] = "；", ["'"] = "’",
        ["["] = "【", ["]"] = "】", ["`"] = "·"
    };

    public OnScreenKeyboard(bool chineseEnabled = false, bool speechEnabled = false)
    {
        _chineseEnabled = chineseEnabled;
        _speechEnabled = speechEnabled;
        Dock = DockStyle.Fill;
        BackColor = BgColor;
        BuildLayout();
    }

    // ---------------- 布局 ----------------

    private void BuildLayout()
    {
        var main = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(6),
            BackColor = BgColor
        };
        for (var i = 0; i < 5; i++)
            main.RowStyles.Add(new RowStyle(SizeType.Percent, 20f));
        main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        main.Controls.Add(BuildRow(Row0()), 0, 0);
        main.Controls.Add(BuildRow(Row1()), 0, 1);
        main.Controls.Add(BuildRow(Row2()), 0, 2);
        main.Controls.Add(BuildRow(Row3()), 0, 3);
        main.Controls.Add(BuildRow(Row4()), 0, 4);
        Controls.Add(main);
    }

    private record KeyDef(string Label, float Units, bool Alpha = false, Action? OnClick = null,
        bool Special = false, bool Holdable = false);

    private static KeyDef TextKey(string label, float units = 1f, bool alpha = false) =>
        new(label, units, alpha);

    private IEnumerable<KeyDef> Row0()
    {
        foreach (var c in "1234567890-=") yield return TextKey(c.ToString());
        yield return new KeyDef("⌫", 2f, OnClick: () => Emit(SpecialKey.Backspace), Special: true);
    }

    private IEnumerable<KeyDef> Row1()
    {
        yield return new KeyDef("Tab", 1.5f, OnClick: () => Emit(SpecialKey.Tab), Special: true);
        foreach (var c in "qwertyuiop") yield return TextKey(c.ToString(), alpha: true);
        foreach (var c in "[]\\") yield return TextKey(c.ToString());
    }

    private IEnumerable<KeyDef> Row2()
    {
        yield return new KeyDef("Shift", 1.8f, OnClick: ToggleShift, Special: true);
        foreach (var c in "asdfghjkl") yield return TextKey(c.ToString(), alpha: true);
        yield return TextKey(";");
        yield return TextKey("'");
        yield return new KeyDef("Enter", 2.2f, OnClick: () => Emit(SpecialKey.Enter), Special: true);
    }

    private IEnumerable<KeyDef> Row3()
    {
        yield return new KeyDef("#+=", 2.5f, OnClick: ToggleSymbols, Special: true);
        foreach (var c in "zxcvbnm") yield return TextKey(c.ToString(), alpha: true);
        foreach (var c in ",./") yield return TextKey(c.ToString());
        yield return new KeyDef("Shift", 2.5f, OnClick: ToggleShift, Special: true);
    }

    private IEnumerable<KeyDef> Row4()
    {
        if (_chineseEnabled)
            yield return new KeyDef("中", 1.6f, OnClick: ToggleChinese, Special: true);
        if (_speechEnabled)
            yield return new KeyDef("按住说话", 2.4f, Special: true, Holdable: true);
        yield return new KeyDef("空格", 8f, OnClick: () => Emit(SpecialKey.Space), Special: true);
        yield return new KeyDef("隐藏键盘", 2.5f,
            OnClick: () => HideRequested?.Invoke(this, EventArgs.Empty), Special: true);
    }

    private Control BuildRow(IEnumerable<KeyDef> keys)
    {
        var defs = keys.ToList();
        var total = defs.Sum(d => d.Units);
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 1,
            ColumnCount = defs.Count,
            Margin = Padding.Empty,
            BackColor = BgColor
        };
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        for (var i = 0; i < defs.Count; i++)
        {
            var def = defs[i];
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, def.Units / total * 100f));
            var btn = MakeButton(def);
            row.Controls.Add(btn, i, 0);

            if (def.Alpha) _alphaKeys.Add(btn);
            if (def.Label == "Shift") _shiftKeys.Add(btn);
            if (def.Label == "#+=") _symbolToggle = btn;
            if (def.Label == "中") _langKey = btn;
            if (def.Holdable) _voiceKey = btn;
        }
        return row;
    }

    private Button MakeButton(KeyDef def)
    {
        var btn = new NonSelectableButton
        {
            Text = def.Label,
            Dock = DockStyle.Fill,
            Margin = new Padding(3),
            FlatStyle = FlatStyle.Flat,
            BackColor = def.Special ? SpecialColor : KeyColor,
            ForeColor = FgColor,
            Font = KeyFont,
            TabStop = false,
            Tag = def.Alpha ? "alpha" : null
        };
        btn.FlatAppearance.BorderColor = Color.FromArgb(70, 70, 80);

        if (def.Holdable)
        {
            // 按住说话：按下开始录音，松开结束
            btn.MouseDown += (s, e) =>
            {
                btn.BackColor = Color.FromArgb(190, 60, 60);
                VoiceStart?.Invoke(this, EventArgs.Empty);
            };
            btn.MouseUp += (s, e) =>
            {
                btn.BackColor = SpecialColor;
                VoiceStop?.Invoke(this, EventArgs.Empty);
            };
            return btn;
        }

        if (def.OnClick != null)
        {
            var act = def.OnClick;
            btn.Click += (s, e) => act();
        }
        else
        {
            btn.Click += (s, e) => EmitText(((Button)s!).Text);
        }
        return btn;
    }

    // ---------------- 模式切换 ----------------

    private void ToggleShift()
    {
        if (_chineseMode) return; // 中文模式下 Shift 无意义
        _shift = !_shift;
        UpdateKeyLabels();
    }

    private void ToggleSymbols()
    {
        _symbols = !_symbols;
        if (_symbolToggle != null) _symbolToggle.Text = _symbols ? "ab" : "#+=";
        UpdateKeyLabels();
    }

    private void ToggleChinese()
    {
        _chineseMode = !_chineseMode;
        ClearComposition();
        if (_langKey != null)
        {
            _langKey.Text = _chineseMode ? "EN" : "中";
            _langKey.BackColor = _chineseMode ? Color.FromArgb(80, 110, 200) : SpecialColor;
        }
        _shift = false;
        UpdateKeyLabels();
    }

    private void UpdateKeyLabels()
    {
        var letters = ("qwertyuiop" + "asdfghjkl" + "zxcvbnm").ToCharArray();
        var symbols = SymRowQ.Concat(SymRowA).Concat(SymRowZ).ToArray();
        for (var i = 0; i < _alphaKeys.Count && i < letters.Length; i++)
        {
            _alphaKeys[i].Text = _symbols
                ? symbols[i]
                : (_shift && !_chineseMode ? letters[i].ToString().ToUpper() : letters[i].ToString());
        }
        foreach (var sk in _shiftKeys)
            sk.BackColor = _shift && !_chineseMode ? Color.FromArgb(80, 110, 200) : SpecialColor;
    }

    // ---------------- 按键分发 ----------------

    private void EmitText(string text)
    {
        if (_chineseMode)
        {
            // 字母：进入拼音组合
            if (text.Length == 1 && text[0] is >= 'a' and <= 'z')
            {
                _composition += text;
                UpdateCandidates();
                return;
            }
            // 标点：先上屏首选候选，再输出中文标点
            if (_composition.Length > 0)
                PickCandidate(0);
            text = PunctMap.TryGetValue(text, out var zh) ? zh : text;
        }
        KeyPressed?.Invoke(this, new KioskKeyEventArgs { Text = text });
        if (_shift) { _shift = false; UpdateKeyLabels(); }
    }

    private void Emit(SpecialKey key)
    {
        if (_chineseMode && _composition.Length > 0)
        {
            switch (key)
            {
                case SpecialKey.Backspace:
                    _composition = _composition[..^1];
                    UpdateCandidates();
                    return;
                case SpecialKey.Space:
                    PickCandidate(0);
                    return;
                case SpecialKey.Enter:
                    var raw = _composition;
                    ClearComposition();
                    KeyPressed?.Invoke(this, new KioskKeyEventArgs { Text = raw });
                    return;
            }
        }
        if (key == SpecialKey.Space) { EmitText(" "); return; }
        KeyPressed?.Invoke(this, new KioskKeyEventArgs { Special = key });
    }

    // ---------------- 候选状态（悬浮栏由宿主显示） ----------------

    private void UpdateCandidates()
    {
        _candidates = _composition.Length == 0
            ? new List<string>()
            : PinyinEngine.Instance.GetCandidates(_composition, 200);
        CompositionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void PickCandidate(int index)
    {
        if (index < 0 || index >= _candidates.Count) return;
        var text = _candidates[index];
        _composition = "";
        _candidates.Clear();
        CompositionChanged?.Invoke(this, EventArgs.Empty);
        KeyPressed?.Invoke(this, new KioskKeyEventArgs { Text = text });
    }

    public void ClearComposition()
    {
        _composition = "";
        _candidates.Clear();
        CompositionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>点击不夺取焦点的按钮，避免 WebView 失焦导致键盘收起。</summary>
    private sealed class NonSelectableButton : Button
    {
        public NonSelectableButton()
        {
            SetStyle(ControlStyles.Selectable, false);
        }
    }
}
