using System.Text.Json;

namespace KioskBrowser.Shared;

public class KioskConfig
{
    public string Version { get; set; } = "1.0";
    public BrowserConfig Browser { get; set; } = new();
    public SettingsEntryConfig SettingsEntry { get; set; } = new();
    public AutoLoginConfig AutoLogin { get; set; } = new();
    public AuthConfig Auth { get; set; } = new();
    public KeyboardConfig Keyboard { get; set; } = new();
    public SpeechConfig Speech { get; set; } = new();
    public WatchdogConfig Watchdog { get; set; } = new();
    public RemoteConfig Remote { get; set; } = new();

    /// <summary>开发模式：开启 DevTools / 不拦截功能键。生产环境必须为 false。</summary>
    public bool DevMode { get; set; } = false;

    public KioskConfig Clone()
    {
        var json = JsonSerializer.Serialize(this, ConfigStore.SerializerOptions);
        return JsonSerializer.Deserialize<KioskConfig>(json, ConfigStore.SerializerOptions) ?? new KioskConfig();
    }
}

public class BrowserConfig
{
    public string HomeUrl { get; set; } = "about:blank";

    /// <summary>霸屏模式：全屏锁定+置顶+禁关闭。关闭后为普通窗口（便于测试/调试）。</summary>
    public bool KioskMode { get; set; } = true;

    public bool Fullscreen { get; set; } = true;
    public bool AlwaysOnTop { get; set; } = true;
    public bool BlockSystemKeys { get; set; } = true;

    /// <summary>导航白名单（域名列表，空 = 不限制）。自动包含子域。</summary>
    public List<string> NavigationWhitelist { get; set; } = new();
}

public class SettingsEntryConfig
{
    public bool Enabled { get; set; } = true;
    public int ZoneSize { get; set; } = 80;
    public int TapCount { get; set; } = 8;
    public int TimeWindowMs { get; set; } = 5000;
    public int MaxGapMs { get; set; } = 800;
    public int DeviationPx { get; set; } = 20;
    public int MaxAttempts { get; set; } = 5;
    public int LockoutSeconds { get; set; } = 60;

    /// <summary>设置页入口密码哈希（SHA256，空 = 无密码）。可通过设置页或远程 API 修改。</summary>
    public string PasswordHash { get; set; } = "";
}

public class AutoLoginConfig
{
    public bool Enabled { get; set; } = false;
    public string UsernameSelector { get; set; } = "#username";
    public string PasswordSelector { get; set; } = "#password";
    public string SubmitSelector { get; set; } = "#login-btn";
    public int MaxWaitSeconds { get; set; } = 10;
    public int PollIntervalMs { get; set; } = 500;
    public CredentialsConfig Credentials { get; set; } = new();
}

public class CredentialsConfig
{
    /// <summary>AES-256-GCM 加密后的 Base64 字符串</summary>
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}

/// <summary>
/// 正规 SSO 免登（协议级），与模拟登录（AutoLogin）互补：
/// type=ticket 时优先走票据免登，连续失败 MaxFailures 次后回退模拟登录。
/// </summary>
public class AuthConfig
{
    /// <summary>认证方式：none | ticket</summary>
    public string Type { get; set; } = "none";
    public TicketAuthConfig Ticket { get; set; } = new();
}

public class TicketAuthConfig
{
    /// <summary>template = 声明式模板（覆盖主流厂商）；script = JS 插件（私有协议逃生舱）</summary>
    public string Mode { get; set; } = "template";

    /// <summary>mode=script 时的插件文件名（位于 plugins 目录），如 fanwei-e9.js</summary>
    public string Script { get; set; } = "";

    public TicketTemplateConfig Template { get; set; } = new();

    /// <summary>自定义参数（模板变量 / 脚本 ctx.config 用），非密。如 appId、userId、host。</summary>
    public Dictionary<string, string> Params { get; set; } = new();

    /// <summary>AES-256-GCM 加密后的 appSecret（变量 {secret} / ctx.config.secret）</summary>
    public string Secret { get; set; } = "";

    /// <summary>连续取票失败达到此次数 → 回退模拟登录 + Webhook 告警</summary>
    public int MaxFailures { get; set; } = 3;

    /// <summary>导航完成后 URL 包含此串视为会话失效，自动重新取票（空 = 禁用）</summary>
    public string ReAuthWhenUrlContains { get; set; } = "";

    public int TimeoutMs { get; set; } = 15000;
}

public class TicketTemplateConfig
{
    /// <summary>
    /// 流程形态：ticket = 先调取票接口再用票据引导（默认）；
    /// signedUrl = 零请求直签（签名直接拼进免密 URL，如信息发布平台 /sso?...&amp;sig=...）。
    /// </summary>
    public string Flow { get; set; } = "ticket";

    public string ApiUrl { get; set; } = "";

    /// <summary>GET | POST_FORM | POST_JSON</summary>
    public string Method { get; set; } = "POST_JSON";

    /// <summary>请求参数，值支持模板变量：{参数名} {secret} {ts} {ts:ms} {sign}</summary>
    public Dictionary<string, string> Params { get; set; } = new();

    /// <summary>
    /// 签名规则：algo(表达式):格式。algo ∈ md5|sha1|sha256|hmac-sha1|hmac-sha256(hmac以secret为钥)；
    /// 表达式为 标识符/"字面量" 用 + 连接；格式 ∈ lower-hex(默认)|upper-hex|base64。空 = 不签名。
    /// 例：md5(appId+secret+ts):lower-hex
    /// </summary>
    public string SignRule { get; set; } = "";

    /// <summary>
    /// flow=signedUrl 专用：待签名串模板（如 appid={appid}&amp;redirect_url={host}&amp;ts={ts}&amp;userid={userid}，
    /// 参数须按平台要求的 ASCII 升序手工排好；变量原值替换不编码）。
    /// 展开后存入内建变量 {signString} 供 SignRule 引用：hmac-sha1(signString):base64
    /// </summary>
    public string SignStringTemplate { get; set; } = "";

    /// <summary>签名结果放入哪个请求参数</summary>
    public string SignParam { get; set; } = "sign";

    /// <summary>从 JSON 响应提取票据的点路径，如 data.ticket</summary>
    public string ResponseJsonPath { get; set; } = "";

    /// <summary>兜底：正则第 1 捕获组提取票据（响应非 JSON 时）</summary>
    public string ResponseRegex { get; set; } = "";

    /// <summary>拿到票据后如何建立会话</summary>
    public BootstrapConfig Bootstrap { get; set; } = new();
}

/// <summary>会话引导动作配置（值均支持 {ticket} 及其他模板变量）</summary>
public class BootstrapConfig
{
    /// <summary>navigateUrl | cookies | postForm | headers</summary>
    public string Kind { get; set; } = "navigateUrl";

    /// <summary>navigateUrl/postForm/headers 的目标 URL；cookies 为设完 cookie 后的导航 URL</summary>
    public string Url { get; set; } = "";

    /// <summary>kind=postForm：表单字段</summary>
    public Dictionary<string, string> Fields { get; set; } = new();

    /// <summary>kind=headers：附加请求头</summary>
    public Dictionary<string, string> Headers { get; set; } = new();

    /// <summary>kind=cookies：要注入的 cookie 列表</summary>
    public List<CookieSpecConfig> Cookies { get; set; } = new();
}

public class CookieSpecConfig
{
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";
    public string Domain { get; set; } = "";
    public string Path { get; set; } = "/";
}

public class KeyboardConfig
{
    public bool Enabled { get; set; } = true;
    public int HeightPercent { get; set; } = 35;
    public string Layout { get; set; } = "qwerty";
    public bool EnableChinese { get; set; } = false;

    /// <summary>启用的词库：general(通用18万词) / gov(政务) / medical(医疗) / retail(商场)。</summary>
    public List<string> Vocabularies { get; set; } = new() { "general" };

    /// <summary>自定义词库，一行一词。</summary>
    public string CustomVocabulary { get; set; } = "";
}

public class SpeechConfig
{
    public bool Enabled { get; set; } = false;

    /// <summary>模型档位：small(40MB 流式) / medium(75MB 流式中英) / large(230MB 高精度非流式)</summary>
    public string ModelSize { get; set; } = "medium";

    /// <summary>热词加分（越大越容易命中领域词，建议 1.0~3.0）</summary>
    public float HotwordsScore { get; set; } = 1.5f;
}

public class WatchdogConfig
{
    public int HeartbeatIntervalMs { get; set; } = 3000;
    public int MissedHeartbeatsBeforeRestart { get; set; } = 3;
    public int MemoryThresholdMB { get; set; } = 1024;
    public bool AutoStart { get; set; } = true;
    public string WebhookUrl { get; set; } = "";
}

/// <summary>远程管理（付费版功能）。单机版免费：enabled=false 即纯单机。</summary>
public class RemoteConfig
{
    public bool Enabled { get; set; } = false;
    public string ServerUrl { get; set; } = "";
    public string LicenseKey { get; set; } = "";
    public int PollIntervalSec { get; set; } = 15;
}

public static class ConfigStore
{
    public static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static KioskConfig Load()
    {
        AppPaths.Ensure();
        var cfg = TryLoad(AppPaths.ConfigPath)
                  ?? TryLoad(AppPaths.ConfigPath + ".bak")
                  ?? new KioskConfig();
        return cfg;
    }

    private static KioskConfig? TryLoad(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<KioskConfig>(json, SerializerOptions);
        }
        catch (Exception ex)
        {
            Logger.Error($"config load failed: {path}: {ex.Message}");
            return null;
        }
    }

    public static void Save(KioskConfig cfg)
    {
        AppPaths.Ensure();
        var json = JsonSerializer.Serialize(cfg, SerializerOptions);
        var tmp = AppPaths.ConfigPath + ".tmp";
        File.WriteAllText(tmp, json);
        if (File.Exists(AppPaths.ConfigPath))
            File.Copy(AppPaths.ConfigPath, AppPaths.ConfigPath + ".bak", true);
        File.Move(tmp, AppPaths.ConfigPath, true);
        Logger.Info("config saved");
    }
}
