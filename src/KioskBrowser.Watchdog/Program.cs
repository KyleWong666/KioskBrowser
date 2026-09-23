using System.Diagnostics;
using System.IO.Pipes;
using KioskBrowser.Shared;

namespace KioskBrowser.Watchdog;

/// <summary>
/// 守护进程：监控主程序心跳与存活状态，异常时重启。
/// - 进程丢失 → 5s 冷却后立即拉起
/// - 心跳连续丢失（3×3s+宽限）→ 判定卡死，强杀并重启
/// - 内存超阈值 → 重启
/// - 10 分钟内重启超过 10 次 → 熔断 60s 并 Webhook 告警
/// - BYE 消息 → 30s 内不重启（管理员主动退出/维护窗口）
/// </summary>
internal static class Program
{
    private static long _lastHbTick;          // Environment.TickCount64
    private static int _mainPid;
    private static long _suppressUntil;       // BYE 后的重启抑制截止时间
    private static DateTime _lastStart = DateTime.MinValue;
    private static readonly Queue<DateTime> RestartTimes = new();

    private static string MainExePath => Path.Combine(AppContext.BaseDirectory, "KioskBrowser.exe");

    static async Task<int> Main()
    {
        using var mutex = new Mutex(true, @"Global\KioskWatchdog_Mutex", out var created);
        if (!created) return 0;

        AppPaths.Ensure();
        Logger.Init(AppPaths.LogDir, "watchdog");
        Logger.Info("=== KioskWatchdog started ===");

        using var cts = new CancellationTokenSource();
        try { Console.CancelKeyPress += (s, e) => { e.Cancel = true; cts.Cancel(); }; }
        catch { /* WinExe 无控制台时忽略 */ }

        var pipeTask = PipeLoop(cts.Token);
        await MonitorLoop(cts.Token);
        Logger.Info("=== KioskWatchdog stopped ===");
        return 0;
    }

    // ---------------- 监控主循环 ----------------

    private static async Task MonitorLoop(CancellationToken ct)
    {
        var cfg = ConfigStore.Load();
        var cfgLoadedAt = DateTime.Now;
        var tick = 0;

        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(1000, ct); }
            catch (OperationCanceledException) { break; }
            tick++;

            // 每分钟热加载配置（心跳间隔/内存阈值/Webhook 等）
            if ((DateTime.Now - cfgLoadedAt).TotalMinutes >= 1)
            {
                cfg = ConfigStore.Load();
                cfgLoadedAt = DateTime.Now;
            }

            var proc = FindMainProcess();
            if (proc == null)
            {
                var suppressed = Environment.TickCount64 < Interlocked.Read(ref _suppressUntil);
                if (!suppressed && (DateTime.Now - _lastStart).TotalSeconds >= 5)
                    StartMain(cfg);
            }
            else
            {
                using (proc)
                {
                    CheckHeartbeat(proc, cfg);
                    if (tick % 15 == 0)
                        CheckMemory(proc, cfg);
                }
            }
        }
    }

    private static Process? FindMainProcess()
    {
        try
        {
            var procs = Process.GetProcessesByName("KioskBrowser");
            return procs.Length > 0 ? procs[0] : null;
        }
        catch { return null; }
    }

    private static void CheckHeartbeat(Process proc, KioskConfig cfg)
    {
        var lastHb = Interlocked.Read(ref _lastHbTick);
        var hbPid = Volatile.Read(ref _mainPid);
        if (lastHb == 0 || hbPid != proc.Id) return;

        var graceMs = (long)cfg.Watchdog.HeartbeatIntervalMs * cfg.Watchdog.MissedHeartbeatsBeforeRestart + 3000;
        if (Environment.TickCount64 - lastHb > graceMs)
        {
            Logger.Error($"heartbeat lost (pid={proc.Id}), killing and restarting");
            TryKill(proc);
            Interlocked.Exchange(ref _lastHbTick, 0L);
        }
    }

    private static void CheckMemory(Process proc, KioskConfig cfg)
    {
        try
        {
            proc.Refresh();
            var mb = proc.WorkingSet64 / 1048576;
            if (mb > cfg.Watchdog.MemoryThresholdMB)
            {
                Logger.Warning($"memory {mb}MB exceeds threshold {cfg.Watchdog.MemoryThresholdMB}MB, restarting");
                _ = WebhookAlerter.PostAsync(cfg.Watchdog.WebhookUrl,
                    "memory-restart", $"主程序内存 {mb}MB 超过阈值，已重启");
                TryKill(proc);
            }
        }
        catch { /* 进程可能已退出 */ }
    }

    private static void StartMain(KioskConfig cfg)
    {
        _lastStart = DateTime.Now;

        // 熔断：10 分钟内重启超 10 次 → 暂停 60s
        RestartTimes.Enqueue(_lastStart);
        while (RestartTimes.Count > 0 && (_lastStart - RestartTimes.Peek()).TotalMinutes > 10)
            RestartTimes.Dequeue();
        if (RestartTimes.Count > 10)
        {
            Interlocked.Exchange(ref _suppressUntil, Environment.TickCount64 + 60_000);
            RestartTimes.Clear();
            Logger.Fatal("restart storm detected (>10 restarts in 10min), pausing 60s");
            _ = WebhookAlerter.PostAsync(cfg.Watchdog.WebhookUrl,
                "restart-storm", "主程序 10 分钟内重启超过 10 次，守护进程熔断 60 秒");
            return;
        }

        if (!File.Exists(MainExePath))
        {
            Logger.Error($"main exe not found: {MainExePath}");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(MainExePath)
            {
                UseShellExecute = true,
                WorkingDirectory = AppContext.BaseDirectory
            });
            Logger.Info("main process started");
            Interlocked.Exchange(ref _lastHbTick, 0L);
        }
        catch (Exception ex)
        {
            Logger.Error("failed to start main process", ex);
        }
    }

    private static void TryKill(Process proc)
    {
        try { proc.Kill(); proc.WaitForExit(5000); }
        catch (Exception ex) { Logger.Warning($"kill failed: {ex.Message}"); }
    }

    // ---------------- 心跳管道服务 ----------------

    private static async Task PipeLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(Ipc.PipeName, PipeDirection.In,
                    1, PipeTransmissionMode.Message, PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(ct);
                using var reader = new StreamReader(server);
                while (server.IsConnected)
                {
                    var line = await reader.ReadLineAsync();
                    if (line == null) break;
                    HandleMessage(line);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Logger.Error("pipe server error", ex);
                try { await Task.Delay(1000, ct); } catch (OperationCanceledException) { break; }
            }
        }
    }

    private static void HandleMessage(string line)
    {
        var parts = line.Split(' ', 3);
        if (parts.Length < 1) return;

        switch (parts[0])
        {
            case "HB" when parts.Length >= 2 && int.TryParse(parts[1], out var pid):
                Volatile.Write(ref _mainPid, pid);
                Interlocked.Exchange(ref _lastHbTick, Environment.TickCount64);
                break;
            case "BYE":
                Interlocked.Exchange(ref _suppressUntil, Environment.TickCount64 + 30_000);
                Interlocked.Exchange(ref _lastHbTick, 0L);
                Logger.Info("BYE received, restart suppressed for 30s");
                break;
            case "CRASH":
                Logger.Fatal($"main process crashed: {(parts.Length >= 3 ? parts[2] : "")}");
                break;
        }
    }
}
