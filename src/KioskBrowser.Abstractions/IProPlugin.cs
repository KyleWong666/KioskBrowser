namespace KioskBrowser.Abstractions;

/// <summary>
/// 专业版插件契约。开源版主程序启动时反射加载安装目录下的
/// KioskBrowser.Pro.dll（类型 KioskBrowser.Pro.ProPlugin），不存在则按社区版运行。
/// </summary>
public interface IProPlugin : IDisposable
{
    string Name { get; }

    /// <summary>启动插件（远程管理客户端等）。host 由主程序实现。</summary>
    void Start(IProHost host);
}
