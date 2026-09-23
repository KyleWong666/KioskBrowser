using KioskBrowser.Shared;

namespace KioskBrowser;

/// <summary>
/// 设置页密码门：8 连击后若配置了密码则先验证。
/// 全屏遮罩 + 密码框 + 内嵌软键盘（触屏可用，物理键盘也可直接输入）。
/// 连续 5 次错误锁定 60 秒。
/// </summary>
public sealed class PasswordGate : UserControl
{
    public event EventHandler? Passed;
    public event EventHandler? Cancelled;

    private readonly string _expectedHash;
    private readonly TextBox _pwd;
    private readonly Label _err;
    private int _fails;
    private long _lockedUntil;

    private static readonly Color BgColor = Color.FromArgb(18, 18, 24);

    public PasswordGate(string expectedHash)
    {
        _expectedHash = expectedHash;
        Dock = DockStyle.Fill;
        BackColor = BgColor;

        var top = new Panel { Dock = DockStyle.Fill, BackColor = BgColor };
        var box = new TableLayoutPanel
        {
            Dock = DockStyle.None,
            Width = DpiHelper.S(560),
            Height = DpiHelper.S(280),
            ColumnCount = 1,
            RowCount = 4,
            Anchor = AnchorStyles.None
        };
        box.RowStyles.Add(new RowStyle(SizeType.Absolute, DpiHelper.S(60)));
        box.RowStyles.Add(new RowStyle(SizeType.Absolute, DpiHelper.S(80)));
        box.RowStyles.Add(new RowStyle(SizeType.Absolute, DpiHelper.S(40)));
        box.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        box.Controls.Add(new Label
        {
            Text = "输入管理密码",
            Dock = DockStyle.Fill,
            ForeColor = Color.White,
            Font = new Font("Microsoft YaHei UI", 16f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter
        }, 0, 0);

        _pwd = new TextBox
        {
            Dock = DockStyle.Fill,
            PasswordChar = '●',
            Font = new Font("Microsoft YaHei UI", 22f),
            TextAlign = HorizontalAlignment.Center,
            BackColor = Color.FromArgb(45, 45, 54),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
        };
        _pwd.KeyDown += (s, e) => { if (e.KeyCode == Keys.Return) { Verify(); e.SuppressKeyPress = true; } };
        box.Controls.Add(_pwd, 0, 1);

        _err = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(255, 120, 120),
            Font = new Font("Microsoft YaHei UI", 11f),
            TextAlign = ContentAlignment.MiddleCenter
        };
        box.Controls.Add(_err, 0, 2);

        var btns = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = BgColor,
            Padding = new Padding(DpiHelper.S(80), 0, 0, 0)
        };
        btns.Controls.Add(MakeBtn("确定", Color.FromArgb(50, 110, 200), Verify));
        btns.Controls.Add(MakeBtn("取消", Color.FromArgb(70, 70, 82),
            () => Cancelled?.Invoke(this, EventArgs.Empty)));
        box.Controls.Add(btns, 0, 3);

        top.Controls.Add(box);
        top.Resize += (s, e) => box.Location = new Point(
            (top.Width - box.Width) / 2, (top.Height - box.Height) / 3);

        // 内嵌软键盘（英文/数字即可输密码）
        var kb = new OnScreenKeyboard(chineseEnabled: false, speechEnabled: false)
        {
            Dock = DockStyle.Bottom,
            Height = DpiHelper.S(280)
        };
        kb.KeyPressed += (s, e) =>
        {
            if (e.Special == SpecialKey.Backspace)
            {
                if (_pwd.Text.Length > 0) _pwd.Text = _pwd.Text[..^1];
            }
            else if (e.Special == SpecialKey.Enter)
            {
                Verify();
            }
            else if (e.Text != null)
            {
                _pwd.Text += e.Text;
            }
            _pwd.Focus();
            _pwd.SelectionStart = _pwd.Text.Length;
        };
        kb.HideRequested += (s, e) => Cancelled?.Invoke(this, EventArgs.Empty);

        Controls.Add(top);
        Controls.Add(kb); // 后添加 → Dock.Bottom 先占位
    }

    private Button MakeBtn(string text, Color bg, Action act)
    {
        var btn = new Button
        {
            Text = text,
            Width = DpiHelper.S(160),
            Height = DpiHelper.S(52),
            Margin = new Padding(0, 4, DpiHelper.S(16), 4),
            FlatStyle = FlatStyle.Flat,
            BackColor = bg,
            ForeColor = Color.White,
            Font = new Font("Microsoft YaHei UI", 12f, FontStyle.Bold)
        };
        btn.Click += (s, e) => act();
        return btn;
    }

    private void Verify()
    {
        if (Environment.TickCount64 < _lockedUntil)
        {
            _err.Text = "已锁定，请稍后再试";
            return;
        }
        if (PasswordHasher.Verify(_pwd.Text, _expectedHash))
        {
            Passed?.Invoke(this, EventArgs.Empty);
            return;
        }
        _fails++;
        _pwd.Text = "";
        if (_fails >= 5)
        {
            _fails = 0;
            _lockedUntil = Environment.TickCount64 + 60_000;
            _err.Text = "连续错误 5 次，锁定 60 秒";
            Logger.Warning("settings password locked out (5 failures)");
        }
        else
        {
            _err.Text = $"密码错误（{_fails}/5）";
        }
    }
}
