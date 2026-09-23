using KioskBrowser.Shared;

namespace KioskBrowser.Abstractions;

/// <summary>
/// 宿主（开源版主程序）暴露给专业版插件的能力。
/// 专业版命令执行器只能通过这些方法操作宿主，不直接碰 WebView/配置存储。
/// </summary>
public interface IProHost
{
    KioskConfig Config { get; }
    void SaveConfig();

    /// <summary>导航到指定 URL。</summary>
    void Navigate(string url);

    void RestartApp();

    /// <summary>退出主程序并停止守护进程。</summary>
    void ShutdownAll();

    /// <summary>整窗截图（含 WebView 内容），返回 JPEG Base64。</summary>
    Task<string> CaptureScreenshotBase64Async();

    /// <summary>热更新输入法词库（引擎热加载 + 热词文件，无需重启）。</summary>
    void ReconfigureVocabulary(List<string> vocabularies, string customVocabulary);

    /// <summary>设置/清除设置页入口密码（空串=清除），内部做 SHA256 哈希。</summary>
    void SetSettingsPassword(string password);

    /// <summary>切到 UI 线程执行。</summary>
    void RunOnUi(Action action);

    /// <summary>在设置面板显示一条消息（面板未打开时忽略）。</summary>
    void ShowSettingsMessage(string msg);
}
