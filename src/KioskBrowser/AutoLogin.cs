using System.Text.Json;
using KioskBrowser.Shared;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace KioskBrowser;

/// <summary>
/// 模拟登录执行器：注入脚本后等待 loginResult 消息，带超时兜底。
/// </summary>
public sealed class AutoLogin
{
    private readonly WebView2 _web;
    private readonly AutoLoginConfig _cfg;

    public AutoLogin(WebView2 web, AutoLoginConfig cfg)
    {
        _web = web;
        _cfg = cfg;
    }

    /// <summary>返回 "success" | "failed" | "notfound" | "timeout"。</summary>
    public async Task<string> RunAsync(string username, string password)
    {
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<CoreWebView2WebMessageReceivedEventArgs> handler = (s, e) =>
        {
            try
            {
                using var doc = JsonDocument.Parse(e.WebMessageAsJson);
                if (doc.RootElement.GetProperty("type").GetString() == "loginResult")
                    tcs.TrySetResult(doc.RootElement.GetProperty("result").GetString() ?? "unknown");
            }
            catch { /* 非 JSON 消息忽略 */ }
        };
        _web.CoreWebView2.WebMessageReceived += handler;
        try
        {
            await _web.CoreWebView2.ExecuteScriptAsync(
                InjectedScripts.AutoLogin(_cfg, username, password));
            var timeout = Task.Delay((_cfg.MaxWaitSeconds + 15) * 1000);
            var done = await Task.WhenAny(tcs.Task, timeout);
            return done == tcs.Task ? tcs.Task.Result : "timeout";
        }
        finally
        {
            _web.CoreWebView2.WebMessageReceived -= handler;
        }
    }
}
