using System.Text.Json;
using KioskBrowser.Shared;

namespace KioskBrowser;

/// <summary>
/// 会话引导动作（开源版 DTO）：navigateUrl | cookies | postForm | headers。
/// 与专业版 SSO 模块之间以 JSON 为契约（KioskBrowser.Sso.dll 返回 JSON，这里解析）。
/// </summary>
public sealed class BootstrapAction
{
    public string Kind { get; set; } = "navigateUrl";
    public string Url { get; set; } = "";
    public Dictionary<string, string> Fields { get; set; } = new();
    public Dictionary<string, string> Headers { get; set; } = new();
    public List<CookieSpecConfig> Cookies { get; set; } = new();

    public static BootstrapAction FromJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("引导动作必须是 JSON 对象");

        var action = new BootstrapAction();
        if (root.TryGetProperty("Kind", out var k0) || root.TryGetProperty("kind", out k0))
            action.Kind = k0.GetString() ?? "navigateUrl";
        if (root.TryGetProperty("Url", out var u0) || root.TryGetProperty("url", out u0))
            action.Url = u0.GetString() ?? "";
        if (root.TryGetProperty("navigateUrl", out var nav) && nav.ValueKind == JsonValueKind.String)
        {
            action.Kind = "navigateUrl";
            action.Url = nav.GetString() ?? "";
        }

        foreach (var (propName, dict) in new[] { ("Fields", action.Fields), ("Headers", action.Headers) })
        {
            if (root.TryGetProperty(propName, out var f) || root.TryGetProperty(char.ToLowerInvariant(propName[0]) + propName[1..], out f))
                if (f.ValueKind == JsonValueKind.Object)
                    foreach (var p in f.EnumerateObject()) dict[p.Name] = p.Value.ToString();
        }
        if (root.TryGetProperty("Cookies", out var c) || root.TryGetProperty("cookies", out c))
            if (c.ValueKind == JsonValueKind.Array)
                foreach (var item in c.EnumerateArray())
                {
                    action.Cookies.Add(new CookieSpecConfig
                    {
                        Name = item.TryGetProperty("Name", out var n) || item.TryGetProperty("name", out n) ? n.GetString() ?? "" : "",
                        Value = item.TryGetProperty("Value", out var v) || item.TryGetProperty("value", out v) ? v.GetString() ?? "" : "",
                        Domain = item.TryGetProperty("Domain", out var d) || item.TryGetProperty("domain", out d) ? d.GetString() ?? "" : "",
                        Path = item.TryGetProperty("Path", out var p2) || item.TryGetProperty("path", out p2) ? p2.GetString() ?? "/" : "/",
                    });
                }

        if (string.IsNullOrEmpty(action.Url) && action.Kind != "cookies")
            throw new InvalidOperationException("引导动作缺少 url");
        if (action.Kind is not ("navigateUrl" or "cookies" or "postForm" or "headers"))
            throw new InvalidOperationException($"未知引导动作 kind: {action.Kind}");
        return action;
    }
}
