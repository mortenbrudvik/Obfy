namespace Obfy.WebView;

/// <summary>
/// How many page reloads after renderer failures count against <see cref="WebViewHandler.MaxReloads"/>. A reload is
/// forgiven once the page has gone <c>stableAfter</c> without another failure, so renderer failures that each recovered
/// over the life of the process never add up to the permanent failure panel, while a page that keeps dying shortly after
/// every reload still runs out. Time since the last failure is the measure, not a navigation completing: a renderer that
/// crashes right after each successful load would otherwise be reloaded for ever. Used on the UI thread only.
/// </summary>
public sealed class ReloadBudget
{
    /// <summary>How long a reloaded page must run without failing for its failure to be forgotten.</summary>
    public static readonly TimeSpan DefaultStableAfter = TimeSpan.FromMinutes(2);

    private readonly TimeSpan _stableAfter;
    private readonly TimeProvider _time;
    private int _used;
    private DateTimeOffset _lastReload;

    public ReloadBudget(TimeSpan? stableAfter = null, TimeProvider? time = null)
    {
        _stableAfter = stableAfter ?? DefaultStableAfter;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>The reloads that still count: those since the page last ran stably.</summary>
    public int Used
    {
        get
        {
            if (_used > 0 && _time.GetUtcNow() - _lastReload >= _stableAfter)
                _used = 0;
            return _used;
        }
    }

    /// <summary>A reload is being made for a failure that just happened.</summary>
    public void Record()
    {
        _used = Used + 1;
        _lastReload = _time.GetUtcNow();
    }
}
