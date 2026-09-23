namespace KioskBrowser;

/// <summary>
/// 候选词栏：悬浮在键盘上方（覆盖页面内容，不挤压键盘布局）。
/// 按钮宽度按文字实测自适应；候选铺满整行，放不下的进下一页，右侧 ◀ ▶ 翻页。
/// </summary>
public sealed class CandidateBar : UserControl
{
    /// <summary>参数为候选在完整列表中的下标。</summary>
    public event EventHandler<int>? CandidatePicked;

    private static readonly Color BarColor = Color.FromArgb(34, 34, 42);
    private static readonly Color KeyColor = Color.FromArgb(52, 52, 60);
    private static readonly Color DisabledColor = Color.FromArgb(30, 30, 36);
    private static readonly Color FgColor = Color.FromArgb(240, 240, 245);
    private static readonly Color AccentColor = Color.FromArgb(120, 170, 255);
    private static readonly Font CandFont = new("Microsoft YaHei UI", 24f, FontStyle.Bold);
    private static readonly Font NavFont = new("Microsoft YaHei UI", 20f, FontStyle.Bold);

    private readonly Label _compLabel;
    private readonly FlowLayoutPanel _flow;
    private readonly Button _prevBtn;
    private readonly Button _nextBtn;
    private readonly Label _pageLabel;

    private List<string> _candidates = new();
    private int _page;
    private int _swipeStartX = -1;

    public CandidateBar()
    {
        Height = DpiHelper.S(124);
        BackColor = BarColor;
        Visible = false;

        _compLabel = new Label
        {
            Dock = DockStyle.Left,
            Width = DpiHelper.S(220),
            ForeColor = AccentColor,
            Font = CandFont,
            TextAlign = ContentAlignment.MiddleCenter
        };

        _prevBtn = MakeNavButton("◀");
        _prevBtn.Dock = DockStyle.Left;
        _prevBtn.Click += (s, e) => { if (_page > 0) { _page--; RenderPage(); } };
        _nextBtn = MakeNavButton("▶");
        _nextBtn.Dock = DockStyle.Right;
        _nextBtn.Click += (s, e) => { _page++; RenderPage(); };
        _pageLabel = new Label
        {
            Dock = DockStyle.Right,
            Width = DpiHelper.S(90),
            ForeColor = FgColor,
            Font = new Font("Microsoft YaHei UI", 12f),
            TextAlign = ContentAlignment.MiddleCenter
        };

        _flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(DpiHelper.S(6), DpiHelper.S(12), DpiHelper.S(6), DpiHelper.S(12)),
            BackColor = BarColor
        };

        // Dock 布局顺序（后添加的先占位）：▶ 最右 → 页码 → ◀ 最左 → 拼音串 → flow 填充
        Controls.Add(_flow);
        Controls.Add(_compLabel);
        Controls.Add(_prevBtn);
        Controls.Add(_pageLabel);
        Controls.Add(_nextBtn);

        // 滑动翻页：左滑下一页，右滑上一页
        foreach (var area in new Control[] { this, _flow, _compLabel })
        {
            area.MouseDown += (s, e) => _swipeStartX = e.X;
            area.MouseUp += (s, e) =>
            {
                if (_swipeStartX < 0) return;
                var dx = e.X - _swipeStartX;
                _swipeStartX = -1;
                if (dx < -60 && _nextBtn.Enabled) { _page++; RenderPage(); }
                else if (dx > 60 && _prevBtn.Enabled) { _page--; RenderPage(); }
            };
        }
    }

    private Button MakeNavButton(string text)
    {
        var btn = new NonSelectableButton
        {
            Text = text,
            Width = DpiHelper.S(90),
            FlatStyle = FlatStyle.Flat,
            BackColor = KeyColor,
            ForeColor = FgColor,
            Font = NavFont,
            TabStop = false,
            Margin = new Padding(0)
        };
        btn.FlatAppearance.BorderColor = Color.FromArgb(70, 70, 80);
        return btn;
    }

    public void SetContent(string composition, IReadOnlyList<string> candidates)
    {
        _candidates = candidates.ToList();
        _page = 0;
        _compLabel.Text = composition;
        RenderPage();
    }

    /// <summary>语音输入状态模式：显示提示文本而非候选列表。</summary>
    public void SetStatus(string text)
    {
        var old = _flow.Controls.Cast<Control>().ToArray();
        _flow.Controls.Clear();
        foreach (var c in old) c.Dispose();
        _candidates = new List<string>();
        _page = 0;
        _pageLabel.Text = "";
        _prevBtn.BackColor = DisabledColor;
        _nextBtn.BackColor = DisabledColor;
        _compLabel.Text = "🎤";
        _flow.Controls.Add(new Label
        {
            Text = text,
            AutoSize = true,
            ForeColor = FgColor,
            Font = CandFont,
            Margin = new Padding(DpiHelper.S(12), DpiHelper.S(28), 0, 0),
            BackColor = BarColor
        });
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (_candidates.Count > 0) RenderPage();
    }

    private void RenderPage()
    {
        var old = _flow.Controls.Cast<Control>().ToArray();
        _flow.Controls.Clear();
        foreach (var c in old) c.Dispose();

        // 可用宽度直接用 flow 实测（含 DPI 缩放），不再按常量估算
        var available = _flow.ClientSize.Width - _flow.Padding.Horizontal - 12;
        if (available < 200) available = Math.Max(200, Width - 520);
        var totalPages = PageCount(available);

        // 页码夹紧 + 空候选兜底
        _page = Math.Clamp(_page, 0, Math.Max(0, totalPages - 1));
        _pageLabel.Text = totalPages > 1 ? $"{_page + 1}/{totalPages}" : "";
        // 注意：按钮始终保持 Enabled=true（disabled 控件点击会穿透到下层 WebView 导致键盘收起），
        // 仅用颜色表示不可用，点击时由 RenderPage 前的页码夹紧兜底。
        _prevBtn.BackColor = _page > 0 ? KeyColor : DisabledColor;
        _nextBtn.BackColor = _page < totalPages - 1 ? KeyColor : DisabledColor;

        if (_candidates.Count == 0) return;

        // 本页能放多少：按文字实测宽度逐个排，直到放不下
        var start = Math.Min(FirstIndexOfPage(_page, available), _candidates.Count - 1);
        var used = 0;
        var end = start;
        while (end < _candidates.Count)
        {
            var w = MeasureButtonWidth(_candidates[end]);
            if (end > start && used + w > available) break;
            used += w;
            end++;
        }
        if (end == start) end = start + 1; // 超长词也至少显示一个

        for (var i = start; i < end; i++)
        {
            var idx = i;
            var btn = new NonSelectableButton
            {
                Text = _candidates[i],
                Width = MeasureButtonWidth(_candidates[i]),
                Height = DpiHelper.S(96),
                Margin = new Padding(DpiHelper.S(6), 1, 0, 1),
                FlatStyle = FlatStyle.Flat,
                BackColor = i == 0 ? Color.FromArgb(60, 80, 130) : KeyColor,
                ForeColor = FgColor,
                Font = CandFont,
                TabStop = false
            };
            btn.FlatAppearance.BorderColor = Color.FromArgb(70, 70, 80);
            btn.Click += (s, e) => CandidatePicked?.Invoke(this, idx);
            _flow.Controls.Add(btn);
        }
    }

    /// <summary>第 page 页第一个候选的下标（逐页宽度累加计算）。</summary>
    private int FirstIndexOfPage(int page, int available)
    {
        var idx = 0;
        for (var p = 0; p < page && idx < _candidates.Count; p++)
        {
            var used = 0;
            var startIdx = idx;
            while (idx < _candidates.Count)
            {
                var w = MeasureButtonWidth(_candidates[idx]);
                if (idx > startIdx && used + w > available) break;
                used += w;
                idx++;
            }
            if (idx == startIdx) idx++;
        }
        return idx;
    }

    private int PageCount(int available)
    {
        var pages = 0;
        var idx = 0;
        while (idx < _candidates.Count)
        {
            pages++;
            var used = 0;
            var startIdx = idx;
            while (idx < _candidates.Count)
            {
                var w = MeasureButtonWidth(_candidates[idx]);
                if (idx > startIdx && used + w > available) break;
                used += w;
                idx++;
            }
            if (idx == startIdx) idx++;
        }
        return Math.Max(1, pages);
    }

    private int MeasureButtonWidth(string text)
    {
        // 用控件真实 Graphics 测量，保证高 DPI 缩放下宽度正确
        using var g = CreateGraphics();
        return TextRenderer.MeasureText(g, text, CandFont).Width + DpiHelper.S(64); // 文字宽 + 左右留白
    }

    private sealed class NonSelectableButton : Button
    {
        public NonSelectableButton()
        {
            SetStyle(ControlStyles.Selectable, false);
        }
    }
}
