using System.Reflection;
using KioskBrowser.Abstractions;
using KioskBrowser.Shared;

namespace KioskBrowser;

/// <summary>
/// 专业版插件桥：反射加载安装目录下的 KioskBrowser.Pro.dll
/// （类型 KioskBrowser.Pro.ProPlugin）。不存在 = 社区版，远程管理不可用。
/// </summary>
public static class ProBridge
{
    public static IProPlugin? TryLoad()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "KioskBrowser.Pro.dll");
            if (!File.Exists(path)) return null;
            var t = Assembly.LoadFrom(path).GetType("KioskBrowser.Pro.ProPlugin");
            if (t == null)
            {
                Logger.Warning("KioskBrowser.Pro.dll 中未找到 ProPlugin 类型");
                return null;
            }
            return Activator.CreateInstance(t) as IProPlugin;
        }
        catch (Exception ex)
        {
            Logger.Error($"pro plugin load failed: {ex.Message}");
            return null;
        }
    }
}
