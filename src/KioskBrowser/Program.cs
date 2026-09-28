using KioskBrowser.Shared;

namespace KioskBrowser;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        // 防多开；"保存并重启"场景下旧实例可能尚未退出，等待重试最多 10 秒
        Mutex? mutex = null;
        for (var i = 0; i < 20; i++)
        {
            var m = new Mutex(false, @"Global\KioskBrowser_Mutex");
            if (m.WaitOne(0))
            {
                mutex = m;
                break;
            }
            m.Dispose();
            Thread.Sleep(500);
        }
        if (mutex == null) return;
        using (mutex)
        {
            RunApp();
        }
    }

    private static void RunApp()
    {
        AppPaths.Ensure();
        Logger.Init(AppPaths.LogDir, "kiosk");
        Logger.Info("=== KioskBrowser starting ===");

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            Logger.Fatal($"unhandled exception: {e.ExceptionObject}");
            HeartbeatClient.ReportCrashSync(e.ExceptionObject?.ToString() ?? "unknown");
        };
        Application.ThreadException += (s, e) =>
        {
            Logger.Error("ui thread exception", e.Exception);
        };
        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            Logger.Error("unobserved task exception", e.Exception);
            e.SetObserved();
        };

        ApplicationConfiguration.Initialize();
        var config = ConfigStore.Load();
        Logger.MinLevel = config.DevMode ? LogLevel.Debug : LogLevel.Info;
        Application.Run(new MainForm(config));
        Logger.Info("=== KioskBrowser exited ===");
    }
}
