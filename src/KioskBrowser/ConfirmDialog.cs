namespace KioskBrowser;

/// <summary>
/// 自定义确认弹窗（无边框模态小窗，居中对齐 owner）。
/// </summary>
public static class ConfirmDialog
{
    public static bool Show(IWin32Window owner, string message, string title = "确认")
    {
        using var form = new Form
        {
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.CenterParent,
            Size = new Size(DpiHelper.S(460), DpiHelper.S(200)),
            BackColor = Color.FromArgb(32, 32, 38),
            TopMost = true,
            ShowInTaskbar = false
        };
        form.Paint += (s, e) =>
        {
            using var pen = new Pen(Color.FromArgb(90, 90, 110), 2);
            e.Graphics.DrawRectangle(pen, 1, 1, form.Width - 3, form.Height - 3);
        };

        var lblTitle = new Label
        {
            Text = title,
            ForeColor = Color.White,
            Font = new Font("Microsoft YaHei UI", 13f, FontStyle.Bold),
            Dock = DockStyle.Top,
            Height = DpiHelper.S(44),
            TextAlign = ContentAlignment.MiddleCenter
        };
        var lblMsg = new Label
        {
            Text = message,
            ForeColor = Color.FromArgb(210, 210, 215),
            Font = new Font("Microsoft YaHei UI", 10.5f),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Padding = new Padding(16, 0, 16, 0)
        };
        var btnOk = MakeButton("确定", Color.FromArgb(200, 70, 70));
        btnOk.DialogResult = DialogResult.OK;
        var btnCancel = MakeButton("取消", Color.FromArgb(70, 70, 82));
        btnCancel.DialogResult = DialogResult.Cancel;

        var btnPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = DpiHelper.S(60),
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 8, 20, 0),
            BackColor = Color.Transparent
        };
        btnPanel.Controls.Add(btnCancel);
        btnPanel.Controls.Add(btnOk);

        form.Controls.Add(lblMsg);
        form.Controls.Add(btnPanel);
        form.Controls.Add(lblTitle);
        form.AcceptButton = btnOk;
        form.CancelButton = btnCancel;

        return form.ShowDialog(owner) == DialogResult.OK;
    }

    private static Button MakeButton(string text, Color bg) => new()
    {
        Text = text,
        Width = DpiHelper.S(110),
        Height = DpiHelper.S(40),
        Margin = new Padding(8, 0, 0, 0),
        FlatStyle = FlatStyle.Flat,
        BackColor = bg,
        ForeColor = Color.White,
        Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold)
    };
}
