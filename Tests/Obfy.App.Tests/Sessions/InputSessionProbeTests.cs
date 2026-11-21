using Microsoft.Extensions.Logging.Abstractions;
using Obfy.App.Models;
using Obfy.App.Services;
using Obfy.App.Sessions;
using Obfy.App.Tests.Common;
using Shouldly;

namespace Obfy.App.Tests.Sessions;

/// <summary>
/// File-system probes (does the saved folder exist, is there an output to open) can stall for a long time on a dead
/// network share. They must not run while the session's lock is held, or every snapshot the page asks for waits behind them.
/// </summary>
public sealed class InputSessionProbeTests
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan SnapshotWait = TimeSpan.FromSeconds(3);

    private readonly FakeSettings _settings = new();
    private readonly FakeShell _shell = new();
    private readonly BlockingProbe _probe = new();

    private InputSession Create() =>
        new(_settings, null, _shell, null, NullLogger<InputSession>.Instance, TimeSpan.Zero, _probe);

    [Fact]
    public async Task ApplyPreferences_DoesNotHoldTheLockWhileProbingTheSavedFolder()
    {
        var session = Create();
        _settings.LastOutputDirectory = @"\\dead-server\share\out";
        _probe.BlockDirectory(@"\\dead-server\share\out");

        var apply = Task.Run(session.ApplyPreferences);
        await _probe.Entered.WaitAsync(Guard);

        // The probe is stuck; the page must still get its snapshot.
        var snapshot = await Task.Run(session.GetSnapshot).WaitAsync(SnapshotWait);
        snapshot.OutputFolder.ShouldBeEmpty();

        _probe.Release();
        await apply.WaitAsync(Guard);
        var after = session.GetSnapshot();
        after.OutputFolder.ShouldBeEmpty();
        after.OutputWarning.ShouldNotBeNull();
    }

    [Fact]
    public async Task ApplyPreferences_ASavedFolderThatIsThere_IsRestored()
    {
        var session = Create();
        _settings.LastOutputDirectory = @"C:\saved";
        _probe.ExistingDirectories.Add(@"C:\saved");

        await Task.Run(session.ApplyPreferences).WaitAsync(Guard);

        session.GetSnapshot().OutputFolder.ShouldBe(@"C:\saved");
        session.GetSnapshot().OutputWarning.ShouldBeNull();
    }

    [Fact]
    public void ApplyPreferences_WithACommandLineFolder_DoesNotProbeTheSavedOne()
    {
        var session = Create();
        session.ApplyStartup(["-o", @"C:\session-out"]);
        _settings.LastOutputDirectory = @"\\dead-server\share\out";
        _probe.BlockDirectory(@"\\dead-server\share\out");

        // Would block until the probe's own guard if it probed.
        session.ApplyPreferences();

        session.GetSnapshot().OutputFolder.ShouldBe(@"C:\session-out");
        _probe.DirectoryProbes.ShouldNotContain(@"\\dead-server\share\out");
    }

    [Fact]
    public async Task OpenOutputFolder_DoesNotHoldTheLockWhileProbing()
    {
        var session = Create();
        session.SetOutputFolder(@"C:\chosen");
        _probe.ExistingDirectories.Add(@"C:\chosen");
        _probe.BlockDirectory(@"C:\chosen");

        var open = Task.Run(session.OpenOutputFolder);
        await _probe.Entered.WaitAsync(Guard);

        var snapshot = await Task.Run(session.GetSnapshot).WaitAsync(SnapshotWait);
        snapshot.OutputFolder.ShouldBe(@"C:\chosen");

        _probe.Release();
        await open.WaitAsync(Guard);
        _shell.Opened.ShouldBe([@"C:\chosen"]);
    }

    [Fact]
    public async Task OpenOutputFolder_NoOutputAndNoFolder_IsStillANotice()
    {
        var session = Create();

        await Task.Run(session.OpenOutputFolder).WaitAsync(Guard);

        session.GetSnapshot().Notices.ShouldHaveSingleItem().Level.ShouldBe(NoticeLevel.Warning);
    }

    /// <summary>A probe over a set of paths that blocks, on request, until the test lets it go.</summary>
    private sealed class BlockingProbe : IPathProbe
    {
        private readonly object _gate = new();
        private readonly List<string> _probed = [];
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ManualResetEventSlim _release = new(false);
        private string? _blocked;

        public HashSet<string> ExistingDirectories { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task Entered => _entered.Task;

        public IReadOnlyList<string> DirectoryProbes
        {
            get
            {
                lock (_gate)
                    return _probed.ToList();
            }
        }

        public void BlockDirectory(string path) => _blocked = path;

        public void Release() => _release.Set();

        public bool FileExists(string path) => false;

        public bool DirectoryExists(string path)
        {
            lock (_gate)
                _probed.Add(path);
            if (string.Equals(path, _blocked, StringComparison.OrdinalIgnoreCase))
            {
                _entered.TrySetResult();
                _release.Wait(Guard);
            }

            return ExistingDirectories.Contains(path);
        }
    }
}
