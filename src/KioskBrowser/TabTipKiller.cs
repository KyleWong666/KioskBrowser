using System.Diagnostics;

namespace KioskBrowser;

/// <summary>
/// 系统虚拟键盘杀手：启动时 + 每 2 秒轮询终止 TabTip.exe / TabTip32.exe。
/// </summary>
public sealed class TabTipKiller : IDisposable
{
    private static readonly string[] Targets = { "TabTip", "TabTip32" };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 2000 };

    public void Start()
    {
        KillNow();
        _timer.Tick += (s, e) => KillNow();
        _timer.Start();
    }

    public void KillNow()
    {
        foreach (var name in Targets)
        {
            Process[] procs;
            try { procs = Process.GetProcessesByName(name); }
            catch { continue; }
            foreach (var p in procs)
            {
                try { p.Kill(); } catch { /* 权限不足或已退出 */ }
                p.Dispose();
            }
        }
    }

    public void Dispose() => _timer.Stop();
}
