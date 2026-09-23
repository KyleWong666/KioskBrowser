using System.Reflection;
using KioskBrowser.Shared;

namespace KioskBrowser;

/// <summary>
/// SSO 模块桥（开源版 ↔ 专业版接缝）：反射加载安装目录下的 KioskBrowser.Sso.dll。
/// 不存在时 auth.type=ticket 直接走降级链（模拟登录兜底），开源版功能不受影响。
/// </summary>
public static class SsoBridge
{
    private static Type? _providerType;
    private static bool _resolved;

    public static bool Available
    {
        get { Resolve(); return _providerType != null; }
    }

    private static void Resolve()
    {
        if (_resolved) return;
        _resolved = true;
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "KioskBrowser.Sso.dll");
            if (!File.Exists(path)) return;
            _providerType = Assembly.LoadFrom(path).GetType("KioskBrowser.Sso.TicketSsoProvider");
            if (_providerType == null)
                Logger.Warning("KioskBrowser.Sso.dll 中未找到 TicketSsoProvider");
        }
        catch (Exception ex)
        {
            Logger.Warning($"sso module load failed: {ex.Message}");
        }
    }

    /// <summary>取一次票，返回引导动作 JSON（由 BootstrapAction.FromJson 解析）。</summary>
    public static async Task<string> AcquireJsonAsync(TicketAuthConfig cfg)
    {
        Resolve();
        if (_providerType == null)
            throw new InvalidOperationException("SSO 模块未安装（KioskBrowser.Sso.dll 为专业版组件）");
        var provider = Activator.CreateInstance(_providerType, cfg)!;
        var task = (Task<string>)_providerType.GetMethod("AcquireJsonAsync")!
            .Invoke(provider, null)!;
        return await task;
    }
}
