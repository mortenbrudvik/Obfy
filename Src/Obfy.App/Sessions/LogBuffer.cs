using System.Globalization;
using Microsoft.Extensions.Logging;
using Obfy.App.Models;
using Obfy.App.Services;
using LogLevel = Obfy.App.Models.LogLevel;

namespace Obfy.App.Sessions;

/// <summary>
/// The run log: lifted from the WPF <c>OutputViewModel</c> without WPF or a dispatcher. A singleton; the run adds lines
/// from any thread and the page reads a <see cref="LogSnapshot"/> and follows <c>log.appended</c>.
/// <para>
/// Every change runs under a lock. Appended lines are not pushed one by one (a run writes many, fast): the first line of
/// a quiet period arms a one-shot timer, and when it fires every line added meanwhile goes out as one
/// <c>log.appended</c> event, outside the lock, on a thread-pool thread. That is safe here because a run only starts
/// after the shell exists; nothing in the callback needs the UI thread beyond <see cref="IEventPublisher.Publish"/>.
/// <see cref="Clear"/> pushes <c>log.cleared</c> at once and drops any batch not yet sent.
/// </para>
/// <para>
/// Pushes can reach the page out of order (the timer and a clear run on different threads), so every payload carries the
/// revision it reflects and every line an id; the page drops what it already holds.
/// </para>
/// </summary>
public sealed class LogBuffer : IDisposable
{
    /// <summary>The line between two runs, as the WPF output panel wrote it.</summary>
    public const string RunSeparator = "———";

    private static readonly TimeSpan DefaultBatchInterval = TimeSpan.FromMilliseconds(100);

    private readonly object _gate = new();
    private readonly List<LogLine> _lines = [];
    private List<LogLine> _pending = [];
    private readonly IEventPublisher? _events;
    private readonly ILogger<LogBuffer>? _logger;
    private readonly TimeSpan _interval;
    private readonly Timer _timer;
    private long _revision;
    private long _nextId = 1;
    private bool _armed;
    private bool _disposed;

    /// <param name="events">Where <c>log.appended</c> and <c>log.cleared</c> go; null for a buffer nobody listens to.</param>
    /// <param name="batchInterval">How long lines wait to be sent together. Default 100 ms. Tests pass a very long one and call <see cref="Flush"/>.</param>
    public LogBuffer(IEventPublisher? events = null, TimeSpan? batchInterval = null, ILogger<LogBuffer>? logger = null)
    {
        _events = events;
        _logger = logger;
        _interval = batchInterval ?? DefaultBatchInterval;
        _timer = new Timer(_ => OnTimer(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Adds a line. Safe from any thread.</summary>
    public void Add(LogLevel level, string message)
    {
        lock (_gate)
            AddLocked(level, message);
    }

    /// <summary>
    /// Marks the start of a run: adds the <see cref="RunSeparator"/> line, but only when the log already holds lines, so
    /// a first run (or one after a clear) starts clean.
    /// </summary>
    public void StartRunSeparator()
    {
        lock (_gate)
        {
            if (_lines.Count > 0)
                AddLocked(LogLevel.Info, RunSeparator);
        }
    }

    private void AddLocked(LogLevel level, string message)
    {
        if (_disposed)
            return;

        var line = new LogLine(
            _nextId++,
            DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture),
            level,
            message);
        _lines.Add(line);
        _pending.Add(line);
        _revision++;

        if (!_armed)
        {
            _armed = true;
            _timer.Change(_interval, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>A copy of every line, oldest first.</summary>
    public IReadOnlyList<LogLine> Get()
    {
        lock (_gate)
            return _lines.ToList();
    }

    /// <summary>The lines with the revision they reflect.</summary>
    public LogSnapshot GetSnapshot()
    {
        lock (_gate)
            return new LogSnapshot(_revision, _lines.ToList());
    }

    /// <summary>Empties the log and pushes <c>log.cleared</c>. Nothing happens, and nothing is pushed, when it is already empty.</summary>
    public void Clear()
    {
        LogSnapshot snapshot;
        lock (_gate)
        {
            if (_lines.Count == 0)
                return;

            _lines.Clear();
            _pending = [];
            _revision++;
            snapshot = new LogSnapshot(_revision, []);
        }

        _events?.Publish(AppEvents.LogCleared, snapshot);
    }

    /// <summary>
    /// Pushes the lines added since the last push now, as one <c>log.appended</c> event; nothing when there are none. The
    /// timer calls this; tests call it to take the timing out.
    /// </summary>
    public void Flush()
    {
        LogAppended batch;
        lock (_gate)
        {
            _armed = false;
            if (_pending.Count == 0)
                return;

            batch = new LogAppended(_revision, _pending);
            _pending = [];
        }

        _events?.Publish(AppEvents.LogAppended, batch);
    }

    private void OnTimer()
    {
        try
        {
            Flush();
        }
        catch (Exception ex)
        {
            // A throw on a timer thread would end the process; the lines are still in the log and log.get returns them.
            _logger?.LogWarning("Failed to push log lines ({ExceptionType})", ex.GetType().Name);
        }
    }

    public void Dispose()
    {
        lock (_gate)
            _disposed = true;
        _timer.Dispose();
    }
}
