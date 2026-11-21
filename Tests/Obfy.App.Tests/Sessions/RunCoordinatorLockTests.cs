using Microsoft.Extensions.Logging.Abstractions;
using Obfy.App;
using Obfy.App.Models;
using Obfy.App.Services;
using Obfy.App.Sessions;
using Obfy.App.Tests.Common;
using Obfy.Core.Models;
using Shouldly;

namespace Obfy.App.Tests.Sessions;

/// <summary>
/// B-45: <c>RunCoordinator.Start</c> used to ask the file system about every listed path (the output-collision check follows
/// links) while it held the lock that <c>IsRunning</c> and <c>EnsureNotRunning</c> wait on, so one input on an unreachable
/// share froze the window's close handler and every bridge call that refuses input during a run. The check now runs outside
/// the lock. These tests drive the one place the check touches the disk, the link question on <see cref="IPathProbe"/>, and
/// pin what must still hold: a run protects exactly the files and settings that were there when it became Running, and
/// the collision verdict is about exactly that list.
/// </summary>
public sealed class RunCoordinatorLockTests
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Prompt = TimeSpan.FromSeconds(3);

    /// <summary>The real file system, except that every link question first runs a hook a test sets.</summary>
    private sealed class LinkProbe : IPathProbe
    {
        public Action? OnLinkQuestion { get; set; }

        public bool FileExists(string path) => File.Exists(path);

        public bool DirectoryExists(string path) => Directory.Exists(path);

        public FileSystemInfo? ResolveLinkTarget(string path)
        {
            OnLinkQuestion?.Invoke();
            return null;
        }
    }

    /// <summary>
    /// Runs on a thread of its own, not a pool thread: xUnit runs classes in parallel and some tests block pool threads on
    /// purpose, so a probe that needs a free pool thread inside a short time limit could fail on a busy machine for a
    /// reason that has nothing to do with the lock.
    /// </summary>
    private static Task<T> OnItsOwnThread<T>(Func<T> work) =>
        Task.Factory.StartNew(work, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

    /// <summary><c>Start</c> on a thread of its own. The task it gives completes when the run ends, or faults with what <c>Start</c> threw.</summary>
    private static Task StartOnItsOwnThread(ReviewHarness h) =>
        Task.Factory.StartNew(() => h.Run.Start(), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();

    /// <summary>Makes a file under the harness folder (and its subfolders) and returns its path, without listing it.</summary>
    private static string Make(ReviewHarness h, string relative)
    {
        var path = Path.Combine(h.Directory, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, []);
        return path;
    }

    /// <summary>A hook that runs <paramref name="action"/> on the first link question only, and not again for the questions it causes itself.</summary>
    private static Action Once(Action action)
    {
        var fired = 0;
        return () =>
        {
            if (Interlocked.Exchange(ref fired, 1) == 0)
                action();
        };
    }

    [Fact]
    public async Task Start_DoesNotHoldTheRunLockWhileTheFileSystemIsAsked()
    {
        var probe = new LinkProbe();
        using var h = new ReviewHarness(pathProbe: probe);
        h.AddFile("a.dll");

        // The link question blocks, as a read from an unreachable share would, on the first call from Start.
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        probe.OnLinkQuestion = Once(() =>
        {
            entered.Set();
            release.Wait(Guard);
        });

        var start = StartOnItsOwnThread(h);
        entered.Wait(Guard).ShouldBeTrue();
        try
        {
            // Stuck inside the check, Start must not be holding what these two wait on.
            (await OnItsOwnThread(() => h.Run.IsRunning).WaitAsync(Prompt)).ShouldBeFalse();
            await OnItsOwnThread(() =>
            {
                h.Run.EnsureNotRunning();
                return true;
            }).WaitAsync(Prompt);
        }
        finally
        {
            release.Set();
        }

        // The task from StartOnItsOwnThread is Start's own, so this also waits for the run itself.
        await start.WaitAsync(Guard);
        h.Engine.ObfuscatedInputs.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Start_AFileRemovedWhileTheCheckRuns_StartsWithTheFilesThatAreLeft_NotOnAStaleCollision()
    {
        var probe = new LinkProbe();
        using var h = new ReviewHarness(pathProbe: probe);
        var a = Make(h, Path.Combine("a", "a.dll"));
        var b = Make(h, Path.Combine("b", "b.dll"));
        h.Input.AddPaths([a, b]);
        h.Input.SetOutputFolder(Path.GetDirectoryName(b)!);

        // With both listed this is a closed set into b's folder, which replaces b.dll: blocked, as the run bar says.
        h.Run.GetSnapshot().BlockedReason!.ShouldStartWith("The output folder is the folder of b.dll;");

        // b.dll is removed while Start is still checking. The list that is left writes b\a.dll, which replaces nothing.
        probe.OnLinkQuestion = Once(() => h.Input.Remove(h.Input.GetSnapshot().Files.Single(f => f.Path == b).Id));
        await StartOnItsOwnThread(h).WaitAsync(Guard);

        h.Engine.ObfuscatedInputs.ShouldBe([a]);
        h.Engine.ClosedSetInputs.ShouldBeEmpty();
    }

    [Fact]
    public async Task Start_AFileAddedWhileTheCheckRuns_IsInTheCheck_SoAStaleCleanResultNeverStartsARun()
    {
        var probe = new LinkProbe();
        using var h = new ReviewHarness(pathProbe: probe);
        var app = h.AddFile("App.dll");
        var other = Make(h, Path.Combine("out", "Other.dll"));
        h.Input.SetOutputFolder(Path.GetDirectoryName(other)!);
        h.Run.GetSnapshot().BlockedReason.ShouldBeNull();

        // Other.dll is listed while Start is checking. Two assemblies are a closed set into out\, which writes out\Other.dll
        // over the file just listed; the answer for the list of one ("nothing is replaced") is stale.
        probe.OnLinkQuestion = Once(() => h.Input.AddPaths([other]));
        var error = await Should.ThrowAsync<ObfyApiException>(() => StartOnItsOwnThread(h).WaitAsync(Guard));

        error.Code.ShouldBe("run.blocked");
        error.Message.ShouldStartWith("The output folder is the folder of Other.dll;");
        h.Run.IsRunning.ShouldBeFalse();
        h.Engine.ObfuscatedInputs.ShouldBeEmpty();
        h.Engine.ClosedSetInputs.ShouldBeEmpty();
        h.Input.GetSnapshot().Files.Select(f => f.Path).ShouldBe([app, other]);
    }

    [Fact]
    public async Task Start_SettingsChangedWhileTheCheckRuns_RunWithTheSettingsThatAreThereWhenItBecomesRunning()
    {
        var probe = new LinkProbe();
        using var h = new ReviewHarness(pathProbe: probe);
        h.AddFile("a.dll");
        ObfySettings? used = null;
        h.Engine.Obfuscate = (_, _, settings, _) =>
        {
            used = settings;
            return Task.FromResult(ObfuscationResult.Successful(new ObfuscationStatistics()));
        };

        probe.OnLinkQuestion = Once(() => h.Protection.ApplyLevel(ObfuscationLevel.Aggressive));
        await StartOnItsOwnThread(h).WaitAsync(Guard);

        used.ShouldNotBeNull().Level.ShouldBe(ObfuscationLevel.Aggressive);
    }

    [Fact]
    public async Task Start_WhenTheListKeepsChanging_GivesUpAfterAFewTriesAndNothingIsLeftRunning()
    {
        var probe = new LinkProbe();
        using var h = new ReviewHarness(pathProbe: probe);
        h.AddFile("a.dll");

        // A change on every link question: no check can finish on a list that is still the list. Bounded, in case the
        // retry were not: the hook stops changing things after a while, which would let a run start and fail the test.
        var changes = 0;
        var busy = 0;
        probe.OnLinkQuestion = () =>
        {
            if (changes >= 200 || Interlocked.Exchange(ref busy, 1) == 1)
                return;
            try
            {
                changes++;
                h.Input.SetWriteSymbolMap(changes % 2 == 1);
            }
            finally
            {
                Volatile.Write(ref busy, 0);
            }
        };

        var error = await Should.ThrowAsync<ObfyApiException>(() => StartOnItsOwnThread(h).WaitAsync(Guard));

        error.Code.ShouldBe("run.blocked");
        error.Message.ShouldBe("The files or settings changed while the run was starting. Try again.");
        changes.ShouldBeLessThan(200);
        h.Run.IsRunning.ShouldBeFalse();
        h.Engine.ObfuscatedInputs.ShouldBeEmpty();

        // Nothing is stuck: once the list holds still, the same Start works.
        probe.OnLinkQuestion = null;
        await StartOnItsOwnThread(h).WaitAsync(Guard);
        h.Engine.ObfuscatedInputs.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Start_WhileARunGoes_IsRefusedWithoutAskingTheFileSystem()
    {
        var probe = new LinkProbe();
        using var h = new ReviewHarness(pathProbe: probe);
        h.AddFile("a.dll");
        var finish = new TaskCompletionSource<ObfuscationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Engine.Obfuscate = (_, _, _, _) =>
        {
            started.TrySetResult();
            return finish.Task;
        };
        var first = h.Run.Start();
        await started.Task.WaitAsync(Guard);

        var questions = 0;
        probe.OnLinkQuestion = () => Interlocked.Increment(ref questions);
        var error = Should.Throw<ObfyApiException>(() => h.Run.Start());

        error.Code.ShouldBe("run.blocked");
        error.Message.ShouldBe("A run is already in progress.");
        questions.ShouldBe(0);

        finish.SetResult(ObfuscationResult.Successful(new ObfuscationStatistics()));
        await first.WaitAsync(Guard);
    }

    // ---- The revisions the check relies on ----

    [Fact]
    public void InputRevision_CountsEveryEditToTheListTheFolderAndTheMap_NotAReadOrACapture()
    {
        using var h = new ReviewHarness();
        var revision = h.Input.Revision;

        h.Input.GetSnapshot();
        h.Input.CaptureRun(merge: false);
        h.Input.ResolveSymbolMapPath();
        h.Input.Revision.ShouldBe(revision);

        void Edited(Action edit)
        {
            var before = h.Input.Revision;
            edit();
            h.Input.Revision.ShouldBeGreaterThan(before);
        }

        Edited(() => h.AddFile("a.dll"));
        var id = h.Input.GetSnapshot().Files.Single().Id;
        Edited(() => h.Input.SetIncluded(id, included: false));
        Edited(() => h.Input.SetIncluded(id, included: true));
        Edited(() => h.Input.SetOutputFolder(Path.Combine(h.Directory, "out")));
        Edited(() => h.Input.SetWriteSymbolMap(true));
        Edited(() => h.Input.SetSymbolMapPath(Path.Combine(h.Directory, "map.json")));
        Edited(() => h.Input.Remove(id));
        Edited(() => h.AddFile("b.dll"));
        Edited(h.Input.Clear);
    }

    [Fact]
    public void InputRevision_CountsTheSessionReadingThePreferences_InTheConstructorAndInApplyPreferences()
    {
        var settings = new FakeSettings { GenerateSymbolMap = true };
        var session = new InputSession(
            settings, null, new FakeShell(), null, NullLogger<InputSession>.Instance, TimeSpan.Zero);

        // Reading the preferences wrote the symbol-map switch, so the session is past its starting revision.
        session.Revision.ShouldBeGreaterThan(0);
        session.CaptureRun(merge: false).SymbolMapPath.ShouldNotBeNull();

        var before = session.Revision;
        settings.GenerateSymbolMap = false;
        session.ApplyPreferences();

        session.Revision.ShouldBeGreaterThan(before);
        session.CaptureRun(merge: false).SymbolMapPath.ShouldBeNull();
    }

    [Fact]
    public async Task ApplyPreferences_ChangesTheStateAndTheRevisionInOneStep_SoNoRevisionHasTwoStates()
    {
        // What Start relies on: a capture at a given revision is always the same state. A capture that landed between
        // the write of the preferences and the bump of the revision would be the old revision with the new state.
        var settings = new FakeSettings();
        var session = new InputSession(
            settings, null, new FakeShell(), null, NullLogger<InputSession>.Instance, TimeSpan.Zero);
        var stateAt = new System.Collections.Concurrent.ConcurrentDictionary<long, bool>();
        var twoStates = 0;
        var writing = true;

        var reader = Task.Factory.StartNew(() =>
        {
            while (Volatile.Read(ref writing))
            {
                var capture = session.CaptureRun(merge: false);
                var mapOn = capture.SymbolMapPath is not null;
                if (stateAt.GetOrAdd(capture.Revision, mapOn) != mapOn)
                    Interlocked.Exchange(ref twoStates, 1);
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        await Task.Factory.StartNew(() =>
        {
            for (var i = 0; i < 50_000 && Volatile.Read(ref twoStates) == 0; i++)
            {
                settings.GenerateSymbolMap = i % 2 == 0;
                session.ApplyPreferences();
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).WaitAsync(Guard);

        Volatile.Write(ref writing, false);
        await reader.WaitAsync(Guard);

        twoStates.ShouldBe(0);
    }

    [Fact]
    public void ACaptureCarriesTheRevisionItWasReadAt()
    {
        using var h = new ReviewHarness();
        h.AddFile("a.dll");

        var capture = h.Input.CaptureRun(merge: false);
        capture.Revision.ShouldBe(h.Input.Revision);

        h.AddFile("b.dll");
        capture.Revision.ShouldBeLessThan(h.Input.Revision);
        h.Input.CaptureRun(merge: false).Revision.ShouldBe(h.Input.Revision);
    }

    [Fact]
    public void ProtectionRevision_CountsEverySettingsChange_AndCaptureSettingsPairsItWithTheCopy()
    {
        using var h = new ReviewHarness();
        var (settings, revision) = h.Protection.CaptureSettings();
        h.Protection.ToObfySettings();
        h.Protection.ValidationErrors();
        h.Protection.Revision.ShouldBe(revision);
        settings.Level.ShouldBe(ObfuscationLevel.Standard);

        h.Protection.ApplyLevel(ObfuscationLevel.Aggressive);

        h.Protection.Revision.ShouldBeGreaterThan(revision);
        var (after, afterRevision) = h.Protection.CaptureSettings();
        afterRevision.ShouldBe(h.Protection.Revision);
        after.Level.ShouldBe(ObfuscationLevel.Aggressive);
        settings.Level.ShouldBe(ObfuscationLevel.Standard); // a copy
    }

    // ---- A close that is approved while Start is still checking ----

    [Fact]
    public async Task Start_WhoseCheckIsStillPendingWhenTheCloseIsApproved_DoesNotBecomeARun()
    {
        var probe = new LinkProbe();
        using var h = new ReviewHarness(pathProbe: probe);
        h.AddFile("a.dll");
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        probe.OnLinkQuestion = Once(() =>
        {
            entered.Set();
            release.Wait(Guard);
        });

        var start = StartOnItsOwnThread(h);
        entered.Wait(Guard).ShouldBeTrue();
        try
        {
            // While Start checks, IsRunning is false, so the shell approves the close without asking the user. From the
            // moment it does, no run may start: the process is on its way out.
            (await OnItsOwnThread(() => h.Run.IsRunning).WaitAsync(Prompt)).ShouldBeFalse();
            (await OnItsOwnThread(h.Run.TryRefuseNewRuns).WaitAsync(Prompt)).ShouldBeTrue();
        }
        finally
        {
            release.Set();
        }

        var error = await Should.ThrowAsync<ObfyApiException>(() => start.WaitAsync(Guard));

        error.Code.ShouldBe("run.blocked");
        error.Message.ShouldBe("Obfy is closing.");
        h.Run.IsRunning.ShouldBeFalse();
        h.Engine.ObfuscatedInputs.ShouldBeEmpty();
        h.Input.GetSnapshot().Files.ShouldAllBe(f => f.Status == FileStatus.Pending);
    }

    [Fact]
    public async Task TryRefuseNewRuns_WhileARunGoes_ChangesNothing_SoACloseTheUserDeclinesLeavesRunsPossible()
    {
        using var h = new ReviewHarness();
        h.AddFile("a.dll");
        var finish = new TaskCompletionSource<ObfuscationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Engine.Obfuscate = (_, _, _, _) =>
        {
            started.TrySetResult();
            return finish.Task;
        };
        var first = h.Run.Start();
        await started.Task.WaitAsync(Guard);

        // The shell asks the user instead; the user says no.
        h.Run.TryRefuseNewRuns().ShouldBeFalse();

        finish.SetResult(ObfuscationResult.Successful(new ObfuscationStatistics()));
        await first.WaitAsync(Guard);
        await h.Run.Start().WaitAsync(Guard);
        h.Engine.ObfuscatedInputs.Count.ShouldBe(2);
    }

    [Fact]
    public async Task AfterRefusingNewRuns_StartIsRefusedAtOnce_AndAllowingThemAgainLetsTheNextStartRun()
    {
        var probe = new LinkProbe();
        using var h = new ReviewHarness(pathProbe: probe);
        h.AddFile("a.dll");
        var questions = 0;
        probe.OnLinkQuestion = () => Interlocked.Increment(ref questions);

        h.Run.RefuseNewRuns();
        var error = Should.Throw<ObfyApiException>(() => h.Run.Start());

        error.Code.ShouldBe("run.blocked");
        error.Message.ShouldBe("Obfy is closing.");
        questions.ShouldBe(0);

        // A restart that could not start a new process leaves Obfy open, and runs must work in it.
        h.Run.AllowNewRuns();
        await h.Run.Start().WaitAsync(Guard);
        h.Engine.ObfuscatedInputs.Count.ShouldBe(1);
    }
}
