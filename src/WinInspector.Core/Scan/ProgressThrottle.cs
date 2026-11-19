namespace WinInspector.Core.Scan;

/// <summary>Allows an action at most once per interval, measured with the injected clock.</summary>
public sealed class ProgressThrottle(TimeSpan interval, Func<TimeSpan> clock)
{
    private TimeSpan? _last;

    /// <summary>True for the first call, then once the interval has passed since the last true.</summary>
    public bool IsDue()
    {
        var now = clock();
        if (_last is { } last && now - last < interval) return false;
        _last = now;
        return true;
    }
}
