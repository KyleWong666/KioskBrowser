namespace KioskBrowser;

/// <summary>
/// DPI 缩放助手：字体(pt)随系统自动缩放，像素尺寸(px)需手动乘系数。
/// 用法：所有硬编码像素尺寸包一层 DpiHelper.S(px)。
/// </summary>
public static class DpiHelper
{
    private static float _factor = -1;

    public static float Factor
    {
        get
        {
            if (_factor < 0)
            {
                using var g = Graphics.FromHwnd(IntPtr.Zero);
                _factor = g.DpiX / 96f;
            }
            return _factor;
        }
    }

    public static int S(int px) => (int)Math.Round(px * Factor);
}
