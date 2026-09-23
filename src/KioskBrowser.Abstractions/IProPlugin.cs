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

    /// <summary>激活状态文案（显示在设置面板）。</summary>
    string ActivationStatusText { get; }

    /// <summary>
    /// 在 parent 容器内显示扫码激活界面。onActivated 参数 = 有效期（可空串）。
    /// </summary>
    void ShowActivationDialog(Control parent, Action<string> onActivated, Action onCancelled);
}
