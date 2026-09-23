using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace KioskBrowser.Shared;

public static class Ipc
{
    public const string PipeName = "KioskBrowserHeartbeat";
}

/// <summary>可选的远程告警：异常/重启事件 HTTP POST 到 Webhook。</summary>
public static class WebhookAlerter
{
    public static async Task PostAsync(string? url, string evt, string message)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var json = JsonSerializer.Serialize(new
            {
                @event = evt,
                message,
                machine = Environment.MachineName,
                time = DateTime.Now.ToString("o")
            });
            await http.PostAsync(url, new StringContent(json, Encoding.UTF8, "application/json"));
        }
        catch (Exception ex)
        {
            Logger.Warning($"webhook post failed: {ex.Message}");
        }
    }
}
