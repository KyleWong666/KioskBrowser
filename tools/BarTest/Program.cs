// 候选栏渲染测试: 真实显示窗口并截屏 (DrawToBitmap 不画子控件, 改为 CopyFromScreen)
using System.Drawing;
using System.Drawing.Imaging;
using KioskBrowser;

ApplicationConfiguration.Initialize();

var outDir = AppContext.BaseDirectory;
var cands = new List<string>
{
    "你好", "昵好", "你号", "尼好", "泥孩", "你好啊", "你好吗", "你", "尼", "呢",
    "泥", "拟", "逆", "倪", "妮", "匿", "霓", "溺", "腻", "鲵",
    "一个特别特别长的词组测试"
};

void Capture(int width, string composition, string file)
{
    using var form = new Form
    {
        Width = width,
        Height = 200,
        StartPosition = FormStartPosition.Manual,
        Location = new Point(0, 0),
        FormBorderStyle = FormBorderStyle.None,
        TopMost = true,
        BackColor = Color.Black
    };
    var bar = new CandidateBar { Dock = DockStyle.Top, Visible = true };
    form.Controls.Add(bar);
    bar.BringToFront();
    form.Show();
    bar.SetContent(composition, cands);
    Application.DoEvents();
    Thread.Sleep(600);
    Application.DoEvents();

    var rect = bar.RectangleToScreen(bar.ClientRectangle);
    using var bmp = new Bitmap(rect.Width, rect.Height);
    using (var g = Graphics.FromImage(bmp))
        g.CopyFromScreen(rect.Location, Point.Empty, rect.Size);
    bmp.Save(Path.Combine(outDir, file), ImageFormat.Png);
    form.Close();
}

Capture(1920, "nihao", "bar_test_w1920.png");
Capture(1280, "ni", "bar_test_w1280.png");
Console.WriteLine("saved");
