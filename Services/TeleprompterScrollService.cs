using System.Windows.Threading;
using TransparentPrompterOverlay.Models;

namespace TransparentPrompterOverlay.Services;

public sealed class TeleprompterScrollService : IDisposable
{
    private readonly DispatcherTimer _timer;
    private IScrollViewport? _viewport;
    private DateTime _lastTickUtc;
    private DateTime _timedStartedUtc;
    private double _timedStartOffset;
    private double _timedTargetOffset;

    public TeleprompterScrollService()
    {
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(33)
        };
        _timer.Tick += Timer_OnTick;
    }

    public event Action? PlaybackStateChanged;

    public TeleprompterMode ActiveMode { get; private set; } = TeleprompterMode.Manual;

    public bool IsRunning => _timer.IsEnabled;

    public double ManualSpeedPixelsPerSecond { get; set; } = 54;

    public TimeSpan TimedDuration { get; set; } = TimeSpan.FromMinutes(3);

    public void AttachViewport(IScrollViewport viewport)
    {
        _viewport = viewport;
    }

    public void Start(TeleprompterMode mode)
    {
        if (_viewport is null)
        {
            return;
        }

        ActiveMode = mode;
        _lastTickUtc = DateTime.UtcNow;

        if (mode == TeleprompterMode.Timed)
        {
            _timedStartedUtc = _lastTickUtc;
            _timedStartOffset = _viewport.CurrentOffset;
            _timedTargetOffset = _viewport.MaxOffset;
        }

        _timer.Start();
        PlaybackStateChanged?.Invoke();
    }

    public void Pause()
    {
        if (!_timer.IsEnabled)
        {
            return;
        }

        _timer.Stop();
        PlaybackStateChanged?.Invoke();
    }

    public void Toggle(TeleprompterMode mode)
    {
        if (IsRunning && ActiveMode == mode)
        {
            Pause();
            return;
        }

        Start(mode);
    }

    public void RestartFromTop()
    {
        _viewport?.ScrollToOffset(0);
        if (ActiveMode == TeleprompterMode.Timed && IsRunning)
        {
            _timedStartedUtc = DateTime.UtcNow;
            _timedStartOffset = 0;
            _timedTargetOffset = _viewport?.MaxOffset ?? 0;
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= Timer_OnTick;
    }

    private void Timer_OnTick(object? sender, EventArgs e)
    {
        if (_viewport is null)
        {
            Pause();
            return;
        }

        var now = DateTime.UtcNow;
        var elapsedSeconds = Math.Max(0.001, (now - _lastTickUtc).TotalSeconds);
        _lastTickUtc = now;

        switch (ActiveMode)
        {
            case TeleprompterMode.Manual:
                var nextOffset = _viewport.CurrentOffset + (ManualSpeedPixelsPerSecond * elapsedSeconds);
                _viewport.ScrollToOffset(nextOffset);
                if (_viewport.CurrentOffset >= _viewport.MaxOffset)
                {
                    Pause();
                }

                break;

            case TeleprompterMode.Timed:
                if (TimedDuration <= TimeSpan.Zero)
                {
                    Pause();
                    return;
                }

                var progress = Math.Clamp((now - _timedStartedUtc).TotalMilliseconds / TimedDuration.TotalMilliseconds, 0, 1);
                var targetOffset = _timedStartOffset + ((_timedTargetOffset - _timedStartOffset) * progress);
                _viewport.ScrollToOffset(targetOffset);
                if (progress >= 1)
                {
                    Pause();
                }

                break;

            default:
                Pause();
                break;
        }
    }
}
