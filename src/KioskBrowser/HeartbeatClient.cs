using System.IO.Pipes;
using KioskBrowser.Shared;

namespace KioskBrowser;

/// <summary>
/// 心跳客户端：通过命名管道向守护进程报告存活状态。
/// 守护进程未运行时静默忽略（主程序可独立运行）。
/// </summary>
public sealed class HeartbeatClient : IDisposable
{
    private NamedPipeClientStream? _pipe;
    private StreamWriter? _writer;

    public Task SendHeartbeatAsync() => SendAsync($"HB {Environment.ProcessId}");
    public Task SendByeAsync() => SendAsync("BYE");

    public async Task SendAsync(string line)
    {
        try
        {
            if (_pipe is not { IsConnected: true })
            {
                Cleanup();
                _pipe = new NamedPipeClientStream(".", Ipc.PipeName,
                    PipeDirection.Out, PipeOptions.Asynchronous);
                await _pipe.ConnectAsync(800);
                _writer = new StreamWriter(_pipe) { AutoFlush = true };
            }
            await _writer!.WriteLineAsync(line);
        }
        catch
        {
            Cleanup(); // 下次重连
        }
    }

    /// <summary>崩溃前的同步临终上报（尽力而为）。</summary>
    public static void ReportCrashSync(string message)
    {
        try
        {
            var safe = message.Replace("\r", " ").Replace("\n", " ");
            if (safe.Length > 500) safe = safe[..500];
            using var pipe = new NamedPipeClientStream(".", Ipc.PipeName, PipeDirection.Out);
            pipe.Connect(500);
            using var writer = new StreamWriter(pipe) { AutoFlush = true };
            writer.WriteLine($"CRASH {Environment.ProcessId} {safe}");
        }
        catch { /* 尽力而为 */ }
    }

    private void Cleanup()
    {
        try { _writer?.Dispose(); } catch { }
        try { _pipe?.Dispose(); } catch { }
        _writer = null;
        _pipe = null;
    }

    public void Dispose() => Cleanup();
}
