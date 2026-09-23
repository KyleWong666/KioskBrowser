using System.Diagnostics;
using System.Runtime.InteropServices;

namespace KioskBrowser;

/// <summary>
/// 低级键盘钩子 (WH_KEYBOARD_LL)，拦截系统快捷键防止退出 Kiosk 模式。
/// 拦截：Win 键、F1~F12、Alt+Tab、Alt+Esc、Ctrl+Esc、Ctrl+Shift+Esc、Ctrl+W、Alt+F4。
/// Ctrl+Alt+Del 无法通过钩子拦截，需配合组策略。
/// </summary>
public sealed class KeyboardHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;

    private const int VK_TAB = 0x09;
    private const int VK_ESCAPE = 0x1B;
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;
    private const int VK_W = 0x57;
    private const int VK_F1 = 0x70;
    private const int VK_F12 = 0x7B;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    private IntPtr _hookId = IntPtr.Zero;
    private LowLevelKeyboardProc? _proc; // 保持引用防止 GC

    /// <summary>开发模式下放行 F1~F12（便于打开 DevTools）。</summary>
    public bool AllowFunctionKeys { get; set; }

    public bool IsInstalled => _hookId != IntPtr.Zero;

    public void Install()
    {
        if (IsInstalled) return;
        _proc = HookCallback;
        using var process = Process.GetCurrentProcess();
        using var module = process.MainModule!;
        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(module.ModuleName), 0);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
        {
            var vk = Marshal.ReadInt32(lParam);
            if (ShouldBlock(vk)) return (IntPtr)1;
        }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private bool ShouldBlock(int vk)
    {
        var mods = Control.ModifierKeys;
        var alt = (mods & Keys.Alt) != 0;
        var ctrl = (mods & Keys.Control) != 0;

        if (vk is VK_LWIN or VK_RWIN) return true;
        if (!AllowFunctionKeys && vk is >= VK_F1 and <= VK_F12) return true; // 含 Alt+F4
        if (vk == VK_TAB && alt) return true;
        if (vk == VK_ESCAPE && (ctrl || alt)) return true; // Ctrl+Esc / Alt+Esc / Ctrl+Shift+Esc
        if (vk == VK_W && ctrl) return true;
        return false;
    }

    public void Dispose()
    {
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
    }
}
