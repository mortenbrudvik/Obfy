using Obfy.App.Models;
using Obfy.App.Tests.Common;
using Obfy.Core.Models;
using Obfy.Core.Models.Solution;
using Shouldly;
using LogLevel = Obfy.App.Models.LogLevel;

namespace Obfy.App.Tests.Sessions;

/// <summary>
/// Regression tests for the code-review findings on <see cref="Obfy.App.Sessions.RunCoordinator"/>: push order, what the file
/// log gets, symbol-map failures and three smaller defects.
/// </summary>
public sealed class RunCoordinatorReviewTests : IDisposable
{
    private readonly ReviewHarness _h = new();

    public void Dispose() => _h.Dispose();

    private static ObfuscationResult Ok(string? output = null, Dictionary<string, string>? map = null) =>
        ObfuscationResult.Successful(new ObfuscationStatistics(), outputPath: output, symbolMap: map);

    private IEnumerable<RunSnapshot> RunPushes() => _h.Events.Published
        .Where(e => e.Name is AppEvents.RunChanged or AppEvents.RunCompleted)
        .Select(e => (RunSnapshot)e.Data!);

    // ---- 1: pushes leave in revision order ----

    [Fact]
    public async Task Cancel_NeverLetsAStaleRunChangedFollowTheFinalSnapshot()
    {
        // The engine ends on another thread once cancelled, as a real one does when it awaits I/O.
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _h.Engine.Obfuscate = (_, _, _, token) =>
        {
            var end = new TaskCompletionSource<ObfuscationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            token.Register(() => end.TrySetCanceled(token));
            started.TrySetResult();
            return end.Task;
        };
        _h.AddFile();
        var run = _h.Run.Start();
        await started.Task.WaitAsync(ReviewHarness.Guard);

        // Hold the first run.changed after cancel is asked; the run thread, once it has cleaned up, is let on only when
        // the cancelling thread is held there.
        using var atGate = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var armed = 0;
        _h.Events.Before = (name, _) =>
        {
            if (Volatile.Read(ref armed) == 0)
                return;
            if (name == AppEvents.RunChanged && Interlocked.Exchange(ref armed, 2) == 1)
            {
                atGate.Set();
                release.Wait(ReviewHarness.Guard);
            }
            else if (name == AppEvents.ResultsChanged)
            {
                atGate.Wait(ReviewHarness.Guard);
            }
        };

        Volatile.Write(ref armed, 1);
        var cancel = Task.Run(() => _h.Run.Cancel());
        atGate.Wait(ReviewHarness.Guard).ShouldBeTrue();

        // Without a publish lock the run ends and pushes its final snapshot now; with one it waits for the held push.
        await Task.WhenAny(run, Task.Delay(400));
        release.Set();
        await cancel.WaitAsync(ReviewHarness.Guard);
        await run.WaitAsync(ReviewHarness.Guard);

        var pushes = RunPushes().ToList();
        pushes.Last().Status.ShouldBe(RunStatus.Cancelled);
        pushes.Select(p => p.Revision).ShouldBe(pushes.Select(p => p.Revision).OrderBy(r => r));
    }

    // ---- 2: failures leave a trace in the file log, type only ----

    [Fact]
    public async Task UnexpectedFailure_IsLoggedByExceptionTypeOnly()
    {
        _h.Engine.Obfuscate = (_, _, _, _) => throw new InvalidOperationException(@"boom at C:\Users\secret\app.dll");
        _h.AddFile();

        await _h.Run.Start().WaitAsync(ReviewHarness.Guard);

        var entries = _h.Logger.Entries;
        entries.ShouldContain(e => e.Level >= Microsoft.Extensions.Logging.LogLevel.Warning && e.Message.Contains(nameof(InvalidOperationException)));
        entries.ShouldAllBe(e => !e.Message.Contains("secret") && e.Exception == null);
    }

    [Fact]
    public async Task ACleanUpStepThatFails_IsLogged_AndTheRunStillEnds()
    {
        var engineReached = false;
        _h.Engine.Obfuscate = (_, _, _, _) =>
        {
            engineReached = true;
            throw new InvalidOperationException("boom");
        };
        _h.AddFile();
        _h.Events.Before = (name, _) =>
        {
            // Only the clean-up after the failure: starting the run publishes results.changed too, and must succeed.
            if (name == AppEvents.ResultsChanged && Volatile.Read(ref engineReached))
                throw new IOException(@"cannot write C:\secret");
        };

        await _h.Run.Start().WaitAsync(ReviewHarness.Guard);

        _h.Run.GetSnapshot().Status.ShouldBe(RunStatus.Faulted);
        var entries = _h.Logger.Entries;
        entries.ShouldContain(e => e.Message.Contains(nameof(IOException)));
        entries.ShouldAllBe(e => !e.Message.Contains("secret") && e.Exception == null);
    }

    [Fact]
    public void AStartThatFails_IsLogged_AndLeavesTheRunFaulted()
    {
        _h.AddFile();
        _h.Events.Before = (name, _) =>
        {
            if (name == AppEvents.ResultsChanged)
                throw new IOException(@"cannot write C:\secret");
        };

        Should.Throw<IOException>(() => _h.Run.Start());

        _h.Run.GetSnapshot().Status.ShouldBe(RunStatus.Faulted);
        var entries = _h.Logger.Entries;
        entries.Count(e => e.Message.Contains(nameof(IOException))).ShouldBeGreaterThanOrEqualTo(2); // the failure and the clean-up that failed again
        entries.ShouldAllBe(e => !e.Message.Contains("secret") && e.Exception == null);
    }

    // ---- 3: the symbol map ----

    private void WantSymbolMap()
    {
        _h.Input.SetWriteSymbolMap(true);
        _h.Input.SetSymbolMapPath(Path.Combine(_h.Directory, "map.json"));
    }

    private static readonly Dictionary<string, string> OneSymbol = new() { ["Foo"] = "a" };

    private void ShouldWarnAboutTheMap(RunSnapshot snapshot)
    {
        snapshot.Status.ShouldBe(RunStatus.Done);
        snapshot.Failed.ShouldBe(0);
        snapshot.Message.ShouldContain("symbol map", Case.Insensitive);
        _h.Input.GetSnapshot().Notices.ShouldContain(n =>
            n.Level == NoticeLevel.Warning && n.Title.Contains("Symbol map", StringComparison.OrdinalIgnoreCase));
        _h.Log.Get().ShouldContain(l => l.Level == LogLevel.Warning && l.Message.Contains("symbol map", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SymbolMapWriteFailure_IsAWarningNoticeAndTheRunBarSaysSo()
    {
        WantSymbolMap();
        _h.Engine.WriteMap = (_, _) => throw new IOException("disk full");
        _h.Engine.Obfuscate = (_, _, _, _) => Task.FromResult(Ok(map: OneSymbol));
        _h.AddFile();

        await _h.Run.Start().WaitAsync(ReviewHarness.Guard);

        ShouldWarnAboutTheMap(_h.Run.GetSnapshot());
    }

    [Fact]
    public async Task SymbolMapFailureThatIsNotIo_KeepsTheRunSucceededAndItsResults()
    {
        WantSymbolMap();
        _h.Engine.WriteMap = (_, _) => throw new ArgumentException("Illegal characters in path.");
        _h.Engine.Obfuscate = (_, _, _, _) => Task.FromResult(Ok(map: OneSymbol));
        _h.AddFile();

        await _h.Run.Start().WaitAsync(ReviewHarness.Guard);

        ShouldWarnAboutTheMap(_h.Run.GetSnapshot());
        _h.Results.GetSnapshot().State.ShouldBe(ResultsState.Finished);
        _h.Results.GetSnapshot().SymbolCount.ShouldBe(1);
        _h.Input.GetSnapshot().Files.Single().Status.ShouldBe(FileStatus.Success);
    }

    [Fact]
    public async Task SymbolMapRequestedButNothingRenamed_IsAWarningNotice()
    {
        WantSymbolMap();
        _h.Engine.Obfuscate = (_, _, _, _) => Task.FromResult(Ok());
        _h.AddFile();

        await _h.Run.Start().WaitAsync(ReviewHarness.Guard);

        _h.Engine.WrittenMaps.ShouldBeEmpty();
        ShouldWarnAboutTheMap(_h.Run.GetSnapshot());
    }

    [Fact]
    public async Task SymbolMapWritten_RaisesNoWarning()
    {
        WantSymbolMap();
        _h.Engine.Obfuscate = (_, _, _, _) => Task.FromResult(Ok(map: OneSymbol));
        _h.AddFile();

        await _h.Run.Start().WaitAsync(ReviewHarness.Guard);

        var snapshot = _h.Run.GetSnapshot();
        snapshot.Message.ShouldBe("Obfuscation complete");
        _h.Input.GetSnapshot().Notices.ShouldBeEmpty();
    }

    [Fact]
    public async Task NoSymbolMapRequested_RaisesNoWarning()
    {
        _h.Engine.Obfuscate = (_, _, _, _) => Task.FromResult(Ok());
        _h.AddFile();

        await _h.Run.Start().WaitAsync(ReviewHarness.Guard);

        _h.Run.GetSnapshot().Message.ShouldBe("Obfuscation complete");
        _h.Input.GetSnapshot().Notices.ShouldBeEmpty();
    }

    // ---- 4: three smaller defects ----

    [Fact]
    public async Task ClosedSet_AnAssemblyWithNeitherResultNorFailure_CountsAsFailed()
    {
        _h.Engine.ClosedSet = inputs => ClosedSetResult.Succeeded(
            [ObfuscationResult.Successful(new ObfuscationStatistics(), inputPath: inputs[0].AssemblyPath, outputPath: inputs[0].AssemblyPath)]);
        _h.AddFile("a.dll");
        _h.AddFile("b.dll");

        await _h.Run.Start().WaitAsync(ReviewHarness.Guard);

        var snapshot = _h.Run.GetSnapshot();
        snapshot.Failed.ShouldBe(1);
        snapshot.Message.ShouldBe("Completed with errors");
        var rows = _h.Input.GetSnapshot().Files;
        rows.Single(r => r.Name == "a.dll").Status.ShouldBe(FileStatus.Success);
        rows.Single(r => r.Name == "b.dll").Status.ShouldBe(FileStatus.Error);
    }

    [Theory]
    [InlineData("OUT.DLL")]
    [InlineData("Out.Exe")]
    public async Task Preview_IsOfferedForAnAssemblyWhateverTheCaseOfItsExtension(string outputName)
    {
        var output = Path.Combine(_h.Directory, outputName);
        File.WriteAllBytes(output, []);
        _h.Engine.Obfuscate = (_, _, _, _) => Task.FromResult(Ok(output));
        _h.AddFile();

        await _h.Run.Start().WaitAsync(ReviewHarness.Guard);

        _h.Results.GetSnapshot().PreviewPaths.ShouldContain(output);
    }

    [Fact]
    public async Task Start_ProtectsTheFilesListedWhenItBecomesRunning_NotOnesRemovedWhileItWasChecking()
    {
        // Start validates the settings, and the key-file probe is where a concurrent files.remove (one that passed its own
        // run.blocked check before the run was Running) lands in the window.
        ReviewHarness? h = null;
        var armed = false;
        var probes = 0;
        h = new ReviewHarness(keyFileExists: _ =>
        {
            if (armed && Interlocked.Increment(ref probes) == 1)
                h!.Input.Remove(h.Input.GetSnapshot().Files.Single(r => r.Name == "b.dll").Id);
            return true;
        });
        using (h)
        {
            h.AddFile("a.dll");
            h.AddFile("b.dll");
            h.Protection.Update(new Dictionary<string, object?> { ["signing.enabled"] = true, ["signing.keyFile"] = "key.snk" });

            armed = true;
            await h.Run.Start().WaitAsync(ReviewHarness.Guard);

            probes.ShouldBeGreaterThan(0);
            h.Input.GetSnapshot().Files.Select(f => f.Name).ShouldBe(["a.dll"]);
            var seen = h.Engine.ObfuscatedInputs.Concat(h.Engine.ClosedSetInputs.SelectMany(x => x)).ToList();
            seen.ShouldNotBeEmpty();
            seen.ShouldAllBe(path => !path.EndsWith("b.dll"));
        }
    }
}
