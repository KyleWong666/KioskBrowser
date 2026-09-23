namespace KioskBrowser.Shared;

public static class AppPaths
{
    public static string Root => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KioskBrowser");

    public static string ConfigPath => Path.Combine(Root, "config.json");
    public static string LogDir => Path.Combine(Root, "logs");
    public static string WebViewDataDir => Path.Combine(Root, "WebView2");

    /// <summary>SSO 脚本插件目录（ticket 认证 mode=script 时加载）。</summary>
    public static string PluginsDir => Path.Combine(Root, "plugins");

    public static void Ensure()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(LogDir);
        Directory.CreateDirectory(PluginsDir);
    }
}
