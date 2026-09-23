namespace KioskBrowser;

/// <summary>
/// 左上角连击触发状态机：
/// 间隔 ≤ MaxGapMs、位移 ≤ DeviationPx、总时长 ≤ TimeWindowMs 内完成 TapCount 次点击 → 触发。
/// 中断（超时/偏离）计为一次失败，连续 MaxAttempts 次失败锁定 LockoutSeconds 秒。
/// </summary>
public sealed class ZoneTapTracker
{
    private readonly int _required;
    private readonly int _windowMs;
    private readonly int _maxGapMs;
    private readonly int _deviation;
    private readonly int _maxFails;
    private readonly int _lockoutSec;

    private int _count;
    private long _firstTick;
    private long _lastTick;
    private int _lastX;
    private int _lastY;
    private int _fails;
    private long _lockoutEnd;

    public event EventHandler? Triggered;

    public ZoneTapTracker(int required, int windowMs, int maxGapMs, int deviation, int maxFails, int lockoutSec)
    {
        _required = required;
        _windowMs = windowMs;
        _maxGapMs = maxGapMs;
        _deviation = deviation;
        _maxFails = maxFails;
        _lockoutSec = lockoutSec;
    }

    public void Tap(int x, int y)
    {
        var now = Environment.TickCount64;
        if (now < _lockoutEnd) return;

        if (_count > 0 && (now - _lastTick > _maxGapMs
                           || now - _firstTick > _windowMs
                           || Math.Abs(x - _lastX) > _deviation
                           || Math.Abs(y - _lastY) > _deviation))
        {
            RegisterFail();
        }

        if (_count == 0)
        {
            _count = 1;
            _firstTick = now;
        }
        else
        {
            _count++;
        }
        _lastTick = now;
        _lastX = x;
        _lastY = y;

        if (_count >= _required)
        {
            _count = 0;
            _fails = 0;
            Triggered?.Invoke(this, EventArgs.Empty);
        }
    }

    private void RegisterFail()
    {
        if (_count >= 2)
        {
            _fails++;
            if (_fails >= _maxFails)
            {
                _fails = 0;
                _lockoutEnd = Environment.TickCount64 + _lockoutSec * 1000L;
            }
        }
        _count = 0;
    }
}
