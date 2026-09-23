// 中文注入端到端测试: 离屏 WebView2 + InsertText("你好") + 回读输入框值
using KioskBrowser;
using Microsoft.Web.WebView2.WinForms;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();

        var form = new Form
        {
            Width = 800,
            Height = 600,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-32000, -32000), // 屏幕外
            ShowInTaskbar = false,
            Opacity = 0
        };
        var web = new WebView2 { Dock = DockStyle.Fill };
        form.Controls.Add(web);

        form.Load += async (s, e) =>
        {
            try
            {
                await web.EnsureCoreWebView2Async();
                web.CoreWebView2.NavigateToString(
                    "<!DOCTYPE html><html><head><meta charset='utf-8'></head>" +
                    "<body><input id='t' type='text'></body></html>");
                await Task.Delay(1000);
                await web.ExecuteScriptAsync("document.getElementById('t').focus()");
                await web.ExecuteScriptAsync(InjectedScripts.InsertText("你好"));
                await web.ExecuteScriptAsync(InjectedScripts.InsertText("a"));
                await web.ExecuteScriptAsync(InjectedScripts.InsertText("中"));
                var val = await web.ExecuteScriptAsync("document.getElementById('t').value");
                Console.WriteLine("RAW=" + val);
                if (val != null)
                {
                    var decoded = System.Text.Json.JsonSerializer.Deserialize<string>(val) ?? "";
                    Console.WriteLine("ESCAPED=" + string.Concat(decoded.Select(c => $"\\u{(int)c:x4}")));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("ERR " + ex.Message);
            }
            Application.Exit();
        };

        Application.Run(form);
    }
}
