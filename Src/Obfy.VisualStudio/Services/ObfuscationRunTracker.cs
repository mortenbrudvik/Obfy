using System;
using System.Collections.Generic;
using System.Threading;

namespace Obfy.VisualStudio.Services;

/// <summary>
/// Knows every obfuscation run that is in flight, so the events that make a run pointless (the solution closes, the
/// package is disposed, a new build of the same project starts) and the Cancel command can stop it. Each run owns its
/// <see cref="CancellationTokenSource"/> and disposes it when the run's scope ends, whatever the outcome.
/// </summary>
public sealed class ObfuscationRunTracker
{
    private readonly object _gate = new();
    private readonly List<ObfuscationRun> _runs = new();
    private bool _shutDown;

    /// <summary>The number of runs that have begun and not yet been disposed.</summary>
    public int ActiveRunCount
    {
        get
        {
            lock (_gate)
                return _runs.Count;
        }
    }

    /// <summary>
    /// Registers a run. Dispose the returned scope when the run ends (<c>using</c>). After <see cref="Shutdown"/> the
    /// run starts out cancelled. <paramref name="projectKey"/> (the project file path) lets
    /// <see cref="CancelProject"/> find it; null means "no particular project".
    /// </summary>
    public ObfuscationRun Begin(string? projectKey)
    {
        var run = new ObfuscationRun(this, projectKey);
        bool cancelNow;
        lock (_gate)
        {
            _runs.Add(run);
            cancelNow = _shutDown;
        }

        if (cancelNow)
            run.Cancel();
        return run;
    }

    /// <summary>Cancels the runs of one project. Returns how many were cancelled.</summary>
    public int CancelProject(string? projectKey)
    {
        if (string.IsNullOrEmpty(projectKey))
            return 0;

        return CancelWhere(run => string.Equals(run.ProjectKey, projectKey, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Cancels every run. Returns how many were cancelled.</summary>
    public int CancelAll() => CancelWhere(_ => true);

    /// <summary>Cancels every run, and every run that begins afterwards. For when the package is disposed.</summary>
    public void Shutdown()
    {
        lock (_gate)
            _shutDown = true;
        CancelAll();
    }

    internal void Remove(ObfuscationRun run)
    {
        lock (_gate)
            _runs.Remove(run);
    }

    private int CancelWhere(Func<ObfuscationRun, bool> match)
    {
        // Cancel outside the lock: a cancelled token runs its callbacks on this thread.
        ObfuscationRun[] snapshot;
        lock (_gate)
            snapshot = _runs.ToArray();

        var cancelled = 0;
        foreach (var run in snapshot)
        {
            if (match(run) && run.Cancel())
                cancelled++;
        }

        return cancelled;
    }
}

/// <summary>One registered run: its cancellation token, valid until the scope is disposed.</summary>
public sealed class ObfuscationRun : IDisposable
{
    private readonly ObfuscationRunTracker _tracker;
    private readonly CancellationTokenSource _source = new();
    private int _disposed;

    internal ObfuscationRun(ObfuscationRunTracker tracker, string? projectKey)
    {
        _tracker = tracker;
        ProjectKey = projectKey;
        Token = _source.Token;
    }

    public string? ProjectKey { get; }

    /// <summary>Taken once, so reading it never throws, even after the run has been disposed.</summary>
    public CancellationToken Token { get; }

    /// <summary>Cancels the run. False when it was already cancelled or has ended.</summary>
    internal bool Cancel()
    {
        try
        {
            if (_source.IsCancellationRequested)
                return false;

            _source.Cancel();
            return true;
        }
        catch (ObjectDisposedException)
        {
            // The run ended between the snapshot and the cancel.
            return false;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _tracker.Remove(this);
        _source.Dispose();
    }
}
