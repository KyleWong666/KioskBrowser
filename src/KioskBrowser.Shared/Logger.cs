namespace KioskBrowser.Shared;

public enum LogLevel { Debug, Info, Warning, Error, Fatal }

/// <summary>
/// 简单文件日志：按天滚动，保留 90 天，线程安全。
/// </summary>
public static class Logger
{
    private static readonly object Sync = new();
    private static string _dir = "";
    private static string _prefix = "app";

    public static LogLevel MinLevel { get; set; } = LogLevel.Info;

    public static void Init(string dir, string prefix)
    {
        _dir = dir;
        _prefix = prefix;
        try
        {
            Directory.CreateDirectory(dir);
            Cleanup();
        }
        catch { /* ignore */ }
    }

    public static void Log(LogLevel level, string message)
    {
        if (level < MinLevel || string.IsNullOrEmpty(_dir)) return;
        try
        {
            var file = Path.Combine(_dir, $"{_prefix}_{DateTime.Now:yyyy-MM-dd}.log");
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level,-7}] {message}{Environment.NewLine}";
            lock (Sync) File.AppendAllText(file, line);
        }
        catch { /* 日志失败不影响主流程 */ }
    }

    public static void Debug(string msg) => Log(LogLevel.Debug, msg);
    public static void Info(string msg) => Log(LogLevel.Info, msg);
    public static void Warning(string msg) => Log(LogLevel.Warning, msg);
    public static void Error(string msg) => Log(LogLevel.Error, msg);
    public static void Error(string msg, Exception ex) => Log(LogLevel.Error, $"{msg}: {ex}");
    public static void Fatal(string msg) => Log(LogLevel.Fatal, msg);

    private static void Cleanup()
    {
        try
        {
            var cutoff = DateTime.Now.AddDays(-90);
            foreach (var f in Directory.GetFiles(_dir, $"{_prefix}_*.log"))
                if (File.GetCreationTime(f) < cutoff)
                    File.Delete(f);
        }
        catch { /* ignore */ }
    }
}
