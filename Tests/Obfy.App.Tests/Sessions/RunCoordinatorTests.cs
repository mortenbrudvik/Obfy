using Microsoft.Extensions.Logging.Abstractions;
using Obfy.App;
using Obfy.App.Features.Files;
using Obfy.App.Features.Run;
using Obfy.App.Features.Settings;
using Obfy.App.Services;
using Obfy.App.Models;
using Obfy.App.Sessions;
using Obfy.App.Tests.Common;
using Obfy.Core.Models;
using Obfy.Core.Models.Solution;
using Obfy.Core.Pipeline;
using Obfy.Core.Services;
using Obfy.Core.Services.Reporting;
using Shouldly;
using LogLevel = Obfy.App.Models.LogLevel;
using ProtectionSession = Obfy.App.Sessions.ProtectionSession;

namespace Obfy.App.Tests.Sessions;

/// <summary>
/// The run half of the WPF <c>MainViewModelTests</c> ported to <see cref="RunCoordinator"/>: 22 of the 30 (command
/// <c>CanExecute</c> became <c>CanStart</c> / <c>BlockedReason</c> in the snapshot, <c>IsObfuscating</c> became
/// <c>Status</c>, status text became <c>Message</c>, snackbars became <c>run.completed</c>). The other eight were not
/// about the run: save/load configuration and preference loading belong to the settings and file sessions (covered
/// there), and the version and Help/About tests belong to the page. Plus the two defects fixed in the lift (UI-052,
/// UI-053) and the tests for what the lift made explicit.
/// <para>
/// The run loop is deterministic: the fake engine completes at once, or waits on a <see cref="TaskCompletionSource"/> the
/// test releases, and every test awaits the run's own task. Nothing sleeps or polls.
/// </para>
/// </summary>
public sealed class RunCoordinatorTests : IDisposable
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(10);

    private readonly FakeEvents _events = new();
    private readonly FakeSettings _preferences = new();
    private readonly FakeEngine _engine = new();
    private readonly FakeReportBuilder _reports = new();
    private readonly FakeAnalyzer _analyzer = new();
    private readonly ProtectionSession _protection;
    private readonly InputSession _input;
    private readonly LogBuffer _log;
    private readonly ResultsStore _results;
    private readonly RunCoordinator _run;
    private readonly string _tempDirectory;

    public RunCoordinatorTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"RunCoordinatorTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);

        _protection = new ProtectionSession(_events);
        _input = new InputSession(_preferences, _analyzer, new FakeShell(), _events, NullLogger<InputSession>.Instance, TimeSpan.Zero);
        _log = new LogBuffer(_events, TimeSpan.FromHours(1));
        _results = new ResultsStore(_events);
        _run = new RunCoordinator(_engine, _reports, _protection, _input, _log, _results, _events);
    }

    public void Dispose()
    {
        _run.Dispose();
        _log.Dispose();
        try
        {
            if (Directory.Exists(_tempDirectory))
                Directory.Delete(_tempDirectory, recursive: true);
        }
        catch (IOException ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }

    // ---- helpers ----

    private string AddFile(string name = "input.dll")
    {
        var path = Path.Combine(_tempDirectory, name);
        File.WriteAllBytes(path, []);
        _input.AddPaths([path]);
        return path;
    }

    /// <summary>A row an analysed solution skipped (a test project), as a drop of a solution would add it.</summary>
    private void AddSkippedFile(string name, string reason)
    {
        var solution = Path.Combine(_tempDirectory, $"{Guid.NewGuid():N}.sln");
        File.WriteAllText(solution, "");
        _analyzer.Entries = path => [ProjectProtectionEntry.Skipped(Path.Combine(_tempDirectory, name), "App.Tests", ProjectSkipReason.Test, reason)];
        _input.AddPaths([solution]);
    }

    /// <summary>A built output from an analysed solution: an included row that came from a session.</summary>
    private string AddSessionAssembly(string name)
    {
        var path = Path.Combine(_tempDirectory, name);
        File.WriteAllBytes(path, []);
        var solution = Path.Combine(_tempDirectory, $"{Guid.NewGuid():N}.sln");
        File.WriteAllText(solution, "");
        _analyzer.Entries = _ => [ProjectProtectionEntry.Included(Path.Combine(_tempDirectory, "App.csproj"), "App", path)];
        _input.AddPaths([solution]);
        return path;
    }

    private IReadOnlyList<FileRow> Rows => _input.GetSnapshot().Files;

    private FileRow Row(string name) => Rows.Single(r => r.Name == name);

    private async Task<RunSnapshot> RunAsync()
    {
        await _run.Start().WaitAsync(Guard);
        return _run.GetSnapshot();
    }

    private static ObfuscationResult Ok(ObfuscationStatistics? stats = null, string? input = null, string? output = null, Dictionary<string, string>? map = null) =>
        ObfuscationResult.Successful(stats ?? new ObfuscationStatistics(), inputPath: input, outputPath: output, symbolMap: map);

    private IReadOnlyList<LogLine> Logs => _log.Get();

    // ---- CanStart / BlockedReason (ported from ObfuscateCommand_CannotExecute_*) ----

    [Fact]
    public void CannotStart_WhenNoFiles()
    {
        var snapshot = _run.GetSnapshot();

        snapshot.CanStart.ShouldBeFalse();
        snapshot.BlockedReason.ShouldNotBeNullOrWhiteSpace();
        snapshot.Mode.ShouldBe(RunMode.None);
    }

    [Fact]
    public void CannotStart_WhenOnlySkippedRows()
    {
        AddSkippedFile("App.Tests.csproj", "Test project");

        _input.GetSnapshot().Files.Count.ShouldBe(1);
        _run.GetSnapshot().CanStart.ShouldBeFalse();
    }

    [Fact]
    public void CannotStart_WhenWatermarkEnabledWithoutId()
    {
        AddFile();
        _run.GetSnapshot().CanStart.ShouldBeTrue();

        _protection.Update(new Dictionary<string, object?> { ["watermark.enabled"] = true, ["watermark.id"] = "" });
        _run.GetSnapshot().CanStart.ShouldBeFalse();

        _protection.UpdateSetting("watermark.id", "customer-42");
        _run.GetSnapshot().CanStart.ShouldBeTrue();
    }

    [Fact]
    public async Task CannotStart_WhileRunning()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource<ObfuscationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _engine.Obfuscate = (_, _, _, _) =>
        {
            started.TrySetResult();
            return finish.Task;
        };
        AddFile();

        var run = _run.Start();
        await started.Task.WaitAsync(Guard);

        var snapshot = _run.GetSnapshot();
        snapshot.Status.ShouldBe(RunStatus.Running);
        snapshot.CanStart.ShouldBeFalse();
        Should.Throw<ObfyApiException>(() => _run.Start()).Code.ShouldBe("run.blocked");

        finish.SetResult(Ok());
        await run.WaitAsync(Guard);
        _run.GetSnapshot().Status.ShouldBe(RunStatus.Done);
    }

    // ---- UI-052: the blockers are explicit state, each with its own reason ----

    [Fact]
    public async Task BlockedReason_Explicit_ForEachBlocker()
    {
        // Nothing included.
        var empty = _run.GetSnapshot();
        empty.CanStart.ShouldBeFalse();
        empty.BlockedReason.ShouldBe("Add at least one included file.");

        // A file makes it runnable, and a runnable run has no reason.
        AddFile();
        var ready = _run.GetSnapshot();
        ready.CanStart.ShouldBeTrue();
        ready.BlockedReason.ShouldBeNull();

        // A validation error blocks, and the reason is the settings' own message, not a copy of the rule.
        _protection.Update(new Dictionary<string, object?> { ["watermark.enabled"] = true, ["watermark.id"] = "" });
        var invalid = _run.GetSnapshot();
        invalid.CanStart.ShouldBeFalse();
        invalid.BlockedReason.ShouldBe(_protection.GetSnapshot().Errors[0].Message);
        invalid.BlockedReason!.ShouldContain("Watermark");
        _protection.UpdateSetting("watermark.id", "customer-42");
        _run.GetSnapshot().BlockedReason.ShouldBeNull();

        // A run in progress blocks another.
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource<ObfuscationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _engine.Obfuscate = (_, _, _, _) =>
        {
            started.TrySetResult();
            return finish.Task;
        };
        var run = _run.Start();
        await started.Task.WaitAsync(Guard);
        var running = _run.GetSnapshot();
        running.CanStart.ShouldBeFalse();
        running.BlockedReason.ShouldNotBeNullOrWhiteSpace();
        running.BlockedReason.ShouldNotBe(empty.BlockedReason);

        finish.SetResult(Ok());
        await run.WaitAsync(Guard);

        // Starting a blocked run says why instead of doing nothing.
        _protection.UpdateSetting("watermark.id", "");
        Should.Throw<ObfyApiException>(() => _run.Start()).Message.ShouldContain("Watermark");
    }

    [Fact]
    public void BlockedReason_IsPushedWhenSettingsOrFilesChange()
    {
        _run.GetSnapshot().CanStart.ShouldBeFalse();

        AddFile();
        var afterFile = _events.Published.Where(e => e.Name == AppEvents.RunChanged).Select(e => (RunSnapshot)e.Data!).Last();
        afterFile.CanStart.ShouldBeTrue();

        _protection.Update(new Dictionary<string, object?> { ["watermark.enabled"] = true, ["watermark.id"] = "" });
        var afterSettings = _events.Published.Where(e => e.Name == AppEvents.RunChanged).Select(e => (RunSnapshot)e.Data!).Last();
        afterSettings.CanStart.ShouldBeFalse();
        afterSettings.Revision.ShouldBeGreaterThan(afterFile.Revision);
    }

    // ---- Success and failure ----

    [Fact]
    public async Task Run_Success_UpdatesStatusAndResults()
    {
        _engine.Obfuscate = (_, _, _, _) => Task.FromResult(Ok(
            new ObfuscationStatistics { StringsEncrypted = 4 },
            map: new Dictionary<string, string> { ["Foo"] = "a" }));
        AddFile();

        var snapshot = await RunAsync();

        snapshot.Status.ShouldBe(RunStatus.Done);
        snapshot.Message.ShouldBe("Obfuscation complete");
        snapshot.Failed.ShouldBe(0);
        snapshot.CanStart.ShouldBeTrue();
        Rows[0].Status.ShouldBe(FileStatus.Success);
        Logs.ShouldContain(l => l.Level == LogLevel.Success);
        _reports.BuildCalls.ShouldBe(1);

        var results = _results.GetSnapshot();
        results.State.ShouldBe(ResultsState.Finished);
        results.SymbolCount.ShouldBe(1);
        results.HasReport.ShouldBeTrue();
        results.HasPreview.ShouldBeFalse();
        results.PreviewError.ShouldBeNull();
        _results.GetSymbolMap().ShouldContainKeyAndValue("Foo", "a");
    }

    [Fact]
    public async Task Run_Success_OffersLastAssemblyOutputForPreview()
    {
        var csOut = Path.Combine(_tempDirectory, "first.cs");
        File.WriteAllText(csOut, "class C {}");
        var dll = typeof(RunCoordinatorTests).Assembly.Location;
        _engine.ClosedSet = (_, _, _, _, _) => Task.FromResult(ClosedSetResult.Succeeded(
            [Ok(output: csOut), Ok(output: dll)]));
        AddFile("a.dll");
        AddFile("b.dll");

        await RunAsync();

        var results = _results.GetSnapshot();
        results.PreviewPaths.ShouldContain(dll);
        results.PreviewPaths.ShouldNotContain(csOut);
        results.PreviewError.ShouldBeNull();
        results.HasPreview.ShouldBeFalse();
        _results.IsKnownPreviewPath(dll).ShouldBeTrue();
    }

    [Fact]
    public async Task Run_Failure_SetsError()
    {
        _engine.Obfuscate = (_, _, _, _) => Task.FromResult(ObfuscationResult.Failed("encrypt failed"));
        AddFile();

        var snapshot = await RunAsync();

        snapshot.Status.ShouldBe(RunStatus.Done);
        snapshot.Message.ShouldBe("Completed with errors");
        snapshot.Failed.ShouldBe(1);
        Rows[0].Status.ShouldBe(FileStatus.Error);
        Rows[0].Error.ShouldBe("encrypt failed");
        Logs.ShouldContain(l => l.Level == LogLevel.Error && l.Message.Contains("encrypt failed"));
    }

    [Fact]
    public async Task Run_Exception_MarksProcessingFileAsError()
    {
        _engine.Obfuscate = (_, _, _, _) => throw new InvalidOperationException("pipeline exploded");
        AddFile();

        var snapshot = await RunAsync();

        snapshot.Status.ShouldBe(RunStatus.Faulted);
        snapshot.Message.ShouldBe("pipeline exploded");
        Rows[0].Status.ShouldBe(FileStatus.Error);
        Rows[0].Error.ShouldBe("pipeline exploded");
        Logs.ShouldContain(l => l.Level == LogLevel.Error && l.Message.Contains("pipeline exploded"));
        _results.GetSnapshot().State.ShouldBe(ResultsState.NotRun);
    }

    [Fact]
    public async Task Run_ReportServiceThrows_FaultsTheRunInsteadOfLeakingTheException()
    {
        _reports.Failure = new InvalidOperationException("report exploded");
        AddFile();

        var snapshot = await RunAsync();

        snapshot.Status.ShouldBe(RunStatus.Faulted);
        snapshot.CanStart.ShouldBeTrue();
    }

    // ---- Cancel ----

    [Fact]
    public async Task Cancel_CancelsRun_AndPutsTheFileBackToPending()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _engine.Obfuscate = async (_, _, _, ct) =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return Ok();
        };
        AddFile();

        var run = _run.Start();
        await started.Task.WaitAsync(Guard);

        _run.Cancel();
        _run.GetSnapshot().Message.ShouldBe("Cancelling...");
        await run.WaitAsync(Guard);

        var snapshot = _run.GetSnapshot();
        snapshot.Status.ShouldBe(RunStatus.Cancelled);
        snapshot.Message.ShouldBe("Cancelled");
        snapshot.CanStart.ShouldBeTrue();
        Rows[0].Status.ShouldBe(FileStatus.Pending);
        Rows[0].Error.ShouldBeNull();
        Logs.ShouldContain(l => l.Message.Contains("cancelled", StringComparison.OrdinalIgnoreCase));
        _results.GetSnapshot().State.ShouldBe(ResultsState.NotRun);
    }

    [Fact]
    public void Cancel_WhenNothingRuns_DoesNothing()
    {
        var before = _run.GetSnapshot().Revision;

        _run.Cancel();

        _run.GetSnapshot().Revision.ShouldBe(before);
        _run.GetSnapshot().Status.ShouldBe(RunStatus.Idle);
    }

    [Fact]
    public async Task Cancel_BetweenFiles_StopsBeforeTheNextOne()
    {
        var calls = new List<string>();
        _engine.Obfuscate = (path, _, _, _) =>
        {
            calls.Add(Path.GetFileName(path));
            _run.Cancel();
            return Task.FromResult(Ok());
        };
        AddFile("a.dll");
        AddFile("b.cs");

        var snapshot = await RunAsync();

        calls.ShouldBe(["a.dll"]);
        snapshot.Status.ShouldBe(RunStatus.Cancelled);
        Row("a.dll").Status.ShouldBe(FileStatus.Success);
        Row("b.cs").Status.ShouldBe(FileStatus.Pending);
    }

    // ---- Per-file progress and status ----

    [Fact]
    public async Task Run_PerFile_ProgressesFileByFile_AndPushesIt()
    {
        var seenWhileProcessing = new List<(string Name, FileStatus Status)>();
        _engine.Obfuscate = (path, _, _, _) =>
        {
            var name = Path.GetFileName(path);
            seenWhileProcessing.Add((name, Row(name).Status));
            return Task.FromResult(Ok());
        };
        AddFile("a.dll");
        AddFile("b.cs");

        var snapshot = await RunAsync();

        snapshot.Mode.ShouldBe(RunMode.Single);
        seenWhileProcessing.ShouldBe([("a.dll", FileStatus.Processing), ("b.cs", FileStatus.Processing)]);
        Rows.ShouldAllBe(r => r.Status == FileStatus.Success);

        // The first pushes are the idle ones the added files caused; the run starts at the first Running one.
        var pushed = _events.Published.Where(e => e.Name == AppEvents.RunChanged).Select(e => (RunSnapshot)e.Data!)
            .SkipWhile(s => s.Status != RunStatus.Running).ToList();
        pushed.First().Status.ShouldBe(RunStatus.Running);
        pushed.Select(s => s.Completed).Distinct().ShouldBe([0, 1, 2]);
        pushed.Select(s => s.Revision).ShouldBe(pushed.Select(s => s.Revision).OrderBy(r => r));
        pushed.Where(s => s.Status == RunStatus.Running).ShouldAllBe(s => s.Total == 2 && !s.Indeterminate);
        pushed.Any(s => s.CurrentFile == "b.cs").ShouldBeTrue();
        pushed.Last().Progress.ShouldBe(100);
        snapshot.Completed.ShouldBe(2);
        snapshot.Total.ShouldBe(2);
        snapshot.CurrentFile.ShouldBeNull();
    }

    [Fact]
    public async Task Run_PublishesCompletedOnce_WithTheFinalSnapshot()
    {
        AddFile();

        await RunAsync();

        var completed = _events.Published.Where(e => e.Name == AppEvents.RunCompleted).ToList();
        completed.Count.ShouldBe(1);
        ((RunSnapshot)completed[0].Data!).Status.ShouldBe(RunStatus.Done);
        AppEvents.RunChanged.ShouldBe("run.changed");
        AppEvents.RunCompleted.ShouldBe("run.completed");
    }

    [Fact]
    public async Task Run_BeginsResults_AndLogsASeparatorOnlyAfterEarlierLines()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource<ObfuscationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _engine.Obfuscate = (_, _, _, _) =>
        {
            started.TrySetResult();
            return finish.Task;
        };
        AddFile();

        var first = _run.Start();
        await started.Task.WaitAsync(Guard);
        _results.GetSnapshot().State.ShouldBe(ResultsState.Running);
        Logs.ShouldNotContain(l => l.Message == LogBuffer.RunSeparator);
        Logs[0].Message.ShouldBe("Starting obfuscation...");
        finish.SetResult(Ok());
        await first.WaitAsync(Guard);

        _engine.Obfuscate = (_, _, _, _) => Task.FromResult(Ok());
        await RunAsync();

        Logs.Count(l => l.Message == LogBuffer.RunSeparator).ShouldBe(1);
        Logs.Count(l => l.Message == "Starting obfuscation...").ShouldBe(2);
    }

    [Fact]
    public async Task Run_RowRemovedWhileRunning_DoesNotFaultTheRun()
    {
        _engine.Obfuscate = (path, _, _, _) =>
        {
            _input.Remove(Row(Path.GetFileName(path)).Id);
            return Task.FromResult(Ok());
        };
        AddFile();

        var snapshot = await RunAsync();

        snapshot.Status.ShouldBe(RunStatus.Done);
        snapshot.Failed.ShouldBe(0);
        Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task Start_WhenAStepBeforeTheTaskThrows_DoesNotStayRunning_AndTheNextStartWorks()
    {
        AddFile();

        // The first push the run makes (run.changed, Running) fails, as a broken event publisher would.
        var thrown = false;
        _events.OnPublish = () =>
        {
            if (thrown)
                return;
            thrown = true;
            throw new InvalidOperationException("publish failed");
        };

        Should.Throw<InvalidOperationException>(() => _run.Start()).Message.ShouldBe("publish failed");

        var stuck = _run.GetSnapshot();
        stuck.Status.ShouldBe(RunStatus.Faulted);
        stuck.Message.ShouldBe("publish failed");
        stuck.CanStart.ShouldBeTrue();
        stuck.BlockedReason.ShouldBeNull();
        _engine.ObfuscateCalls.ShouldBeEmpty();
        _results.GetSnapshot().State.ShouldNotBe(ResultsState.Running);
        _run.Cancel(); // nothing to cancel, and it must not throw
        Logs.ShouldContain(l => l.Level == LogLevel.Error && l.Message.Contains("publish failed"));

        var after = await RunAsync();

        after.Status.ShouldBe(RunStatus.Done);
        _engine.ObfuscateCalls.ShouldHaveSingleItem();
        Rows[0].Status.ShouldBe(FileStatus.Success);
    }

    [Fact]
    public void CaptureRun_ReadsFilesAndPlanTogether()
    {
        AddFile("a.dll");
        AddFile("b.dll");
        AddSkippedFile("App.Tests.csproj", "Test project");

        var capture = _input.CaptureRun(merge: true);

        capture.Included.Select(f => Path.GetFileName(f.Path)).ShouldBe(["a.dll", "b.dll"]);
        capture.Skipped.Select(r => r.Name).ShouldBe(["App.Tests.csproj"]);
        capture.Plan.Mode.ShouldBe(OutputMode.Merge);
        capture.Plan.MergePath!.ShouldContain("a.obfuscated.dll");
        capture.OutputFolderChosen.ShouldBeFalse();
        capture.SymbolMapPath.ShouldBeNull();

        _input.SetWriteSymbolMap(true);
        _input.SetOutputFolder(_tempDirectory);
        var later = _input.CaptureRun(merge: false);
        later.Plan.Mode.ShouldBe(OutputMode.ClosedSet);
        later.OutputFolderChosen.ShouldBeTrue();
        later.SymbolMapPath.ShouldBe(_input.ResolveSymbolMapPath());
    }

    // ---- Merge ----

    [Fact]
    public async Task Run_MergeEnabled_CallsMergeAndObfuscate()
    {
        _protection.UpdateSetting("assemblyMerge.enabled", true);
        var mergedPath = Path.Combine(_tempDirectory, "first.obfuscated.dll");
        _engine.Merge = (_, _, _, _) => Task.FromResult(Ok(new ObfuscationStatistics { TypesRenamed = 1 }, output: mergedPath));
        AddFile("first.dll");
        AddFile("second.dll");

        var snapshot = await RunAsync();

        snapshot.Mode.ShouldBe(RunMode.Merge);
        var call = _engine.MergeCalls.ShouldHaveSingleItem();
        call.Inputs.Count.ShouldBe(2);
        call.Output.ShouldContain("obfuscated", Case.Insensitive);
        _engine.ObfuscateCalls.ShouldBeEmpty();
        _engine.ClosedSetCalls.ShouldBeEmpty();
        Rows.ShouldAllBe(r => r.Status == FileStatus.Success);
        Rows.ShouldAllBe(r => r.OutputPath == mergedPath);
        snapshot.Indeterminate.ShouldBeFalse();
    }

    [Fact]
    public async Task Run_Merge_IsIndeterminateWhileRunning()
    {
        _protection.UpdateSetting("assemblyMerge.enabled", true);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource<ObfuscationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _engine.Merge = (_, _, _, _) =>
        {
            started.TrySetResult();
            return finish.Task;
        };
        AddFile("first.dll");
        AddFile("second.dll");

        var run = _run.Start();
        await started.Task.WaitAsync(Guard);
        _run.GetSnapshot().Indeterminate.ShouldBeTrue();
        Rows.ShouldAllBe(r => r.Status == FileStatus.Processing);
        finish.SetResult(Ok());
        await run.WaitAsync(Guard);
    }

    // ---- Symbol map ----

    [Fact]
    public async Task Run_GenerateSymbolMap_WritesMap()
    {
        var mapPath = Path.Combine(_tempDirectory, "symbolmap.json");
        _input.SetWriteSymbolMap(true);
        _input.SetSymbolMapPath(mapPath);
        _engine.Obfuscate = (_, _, _, _) => Task.FromResult(Ok(map: new Dictionary<string, string> { ["Foo"] = "a" }));
        AddFile();

        await RunAsync();

        var written = _engine.WrittenMaps.ShouldHaveSingleItem();
        written.Path.ShouldBe(mapPath);
        written.Map.ShouldContainKey("Foo");
        Logs.ShouldContain(l => l.Message == $"Symbol map written to {mapPath}");
    }

    [Fact]
    public async Task Run_SymbolMapOff_WritesNothing()
    {
        _engine.Obfuscate = (_, _, _, _) => Task.FromResult(Ok(map: new Dictionary<string, string> { ["Foo"] = "a" }));
        AddFile();

        await RunAsync();

        _engine.WrittenMaps.ShouldBeEmpty();
    }

    [Fact]
    public async Task Run_SymbolMapWriteFailure_IsAWarningNotAFailure()
    {
        _input.SetWriteSymbolMap(true);
        _engine.WriteMap = (_, _) => throw new IOException("disk full");
        _engine.Obfuscate = (_, _, _, _) => Task.FromResult(Ok(map: new Dictionary<string, string> { ["Foo"] = "a" }));
        AddFile();

        var snapshot = await RunAsync();

        snapshot.Status.ShouldBe(RunStatus.Done);
        snapshot.Failed.ShouldBe(0);
        Logs.ShouldContain(l => l.Level == LogLevel.Warning && l.Message.Contains("disk full"));
    }

    // ---- Report ----

    [Fact]
    public async Task Run_MultipleFiles_BuildsReportFromCombinedSymbols()
    {
        _engine.ClosedSet = (_, _, _, _, _) => Task.FromResult(ClosedSetResult.Succeeded(
            [
                Ok(new ObfuscationStatistics { StringsEncrypted = 2 }, map: new Dictionary<string, string> { ["One"] = "a" }),
                Ok(new ObfuscationStatistics { StringsEncrypted = 3 }, map: new Dictionary<string, string> { ["Two"] = "b" })
            ]));
        AddFile("first.dll");
        AddFile("second.dll");

        await RunAsync();

        var captured = _reports.Captured.ShouldHaveSingleItem();
        captured.SymbolMap.Keys.ShouldContain("One");
        captured.SymbolMap.Keys.ShouldContain("Two");
        captured.Statistics.StringsEncrypted.ShouldBe(5);
        _reports.BuildCalls.ShouldBe(1);
    }

    // ---- Mode selection (ported from the ShouldUseClosedSet tests) ----

    [Fact]
    public void Mode_TwoAssemblies_IsClosedSet()
    {
        AddFile("a.dll");
        AddFile("b.dll");

        _run.GetSnapshot().Mode.ShouldBe(RunMode.ClosedSet);
    }

    [Fact]
    public void Mode_OneAssembly_IsSingle()
    {
        AddFile("a.dll");

        _run.GetSnapshot().Mode.ShouldBe(RunMode.Single);
    }

    [Fact]
    public void Mode_OneAssemblyFromASession_IsClosedSet()
    {
        AddSessionAssembly("a.dll");

        _run.GetSnapshot().Mode.ShouldBe(RunMode.ClosedSet);
    }

    [Fact]
    public void Mode_OneAssemblyFromASessionAndASkippedTest_IsClosedSet()
    {
        AddSessionAssembly("App.dll");
        AddSkippedFile("App.Tests.csproj", "Test project");

        _run.GetSnapshot().Mode.ShouldBe(RunMode.ClosedSet);
        _input.GetSnapshot().Files.Count.ShouldBe(2);
    }

    [Fact]
    public void Mode_TwoSourceFiles_IsSources_AndOneIsSingle()
    {
        AddFile("a.cs");
        _run.GetSnapshot().Mode.ShouldBe(RunMode.Single);

        AddFile("b.cs");
        _run.GetSnapshot().Mode.ShouldBe(RunMode.Sources);
    }

    [Fact]
    public void Mode_ExcludingARow_ChangesTheModeAndTheHint()
    {
        AddFile("a.dll");
        var second = AddFile("b.dll");
        _run.GetSnapshot().Mode.ShouldBe(RunMode.ClosedSet);

        _input.SetIncluded(Rows.Single(r => r.Path == second).Id, false);

        var snapshot = _run.GetSnapshot();
        snapshot.Mode.ShouldBe(RunMode.Single);
        snapshot.OutputHint.ShouldBe(_input.GetSnapshot().OutputHint);
    }

    // ---- UI-053: the hint and the run use one rule ----

    [Theory]
    [InlineData("a.dll", false, false, RunMode.Single, "obfuscate")]
    [InlineData("a.dll,b.dll", false, false, RunMode.ClosedSet, "closedSet")]
    [InlineData("a.dll,b.dll", true, false, RunMode.Merge, "merge")]
    [InlineData("a.dll", true, false, RunMode.Single, "obfuscate")]
    [InlineData("a.dll,b.cs", true, false, RunMode.Single, "obfuscate")]
    [InlineData("a.dll,b.dll,c.cs", false, false, RunMode.ClosedSet, "closedSet")]
    [InlineData("a.cs,b.cs", false, false, RunMode.Sources, "sources")]
    [InlineData("a.cs", false, false, RunMode.Single, "obfuscate")]
    [InlineData("a.dll", false, true, RunMode.ClosedSet, "closedSet")]
    public async Task ModeHint_MatchesRunRule(string files, bool merge, bool fromSession, RunMode expected, string expectedCall)
    {
        _protection.UpdateSetting("assemblyMerge.enabled", merge);
        foreach (var name in files.Split(','))
        {
            if (fromSession)
                AddSessionAssembly(name);
            else
                AddFile(name);
        }

        // What the page shows: the mode and the hint, before anything runs.
        var before = _run.GetSnapshot();
        var input = _input.GetSnapshot();
        before.Mode.ShouldBe(expected);
        before.OutputHint.ShouldBe(merge ? input.MergeOutputHint : input.OutputHint);
        before.OutputHint.ShouldBe(_input.PlanOutput(merge).Hint);
        _input.PlanOutput(merge).Mode.ShouldBe(expected switch
        {
            RunMode.Merge => OutputMode.Merge,
            RunMode.ClosedSet => OutputMode.ClosedSet,
            _ => OutputMode.Each
        });

        // What the run does: the engine entry point that matches the mode.
        var after = await RunAsync();
        after.Mode.ShouldBe(expected);
        var calls = new Dictionary<string, int>
        {
            ["obfuscate"] = _engine.ObfuscateCalls.Count,
            ["closedSet"] = _engine.ClosedSetCalls.Count,
            ["merge"] = _engine.MergeCalls.Count,
            ["sources"] = _engine.SourcesCalls.Count
        };
        calls[expectedCall].ShouldBeGreaterThan(0);
        calls.Where(c => c.Key != expectedCall && !(expectedCall == "sources" && c.Key == "obfuscate")).ShouldAllBe(c => c.Value == 0);
    }

    // ---- Closed set ----

    [Fact]
    public async Task Run_TwoAssemblies_CallsClosedSetNotPerFile()
    {
        AddFile("first.dll");
        AddFile("second.dll");

        await RunAsync();

        var call = _engine.ClosedSetCalls.ShouldHaveSingleItem();
        call.Inputs.Count.ShouldBe(2);
        call.Inputs.ShouldAllBe(i => i.Hints != null);
        call.ForcePreservePublic.ShouldBeFalse();
        _engine.ObfuscateCalls.ShouldBeEmpty();
        Rows.ShouldAllBe(r => r.Status == FileStatus.Success);
    }

    [Fact]
    public async Task Run_SkippedFile_StaysSkippedAndLogsReason()
    {
        AddFile("App.dll");
        AddSkippedFile("App.Tests.csproj", "Test project");

        await RunAsync();

        _engine.ObfuscateCalls.ShouldHaveSingleItem();
        _engine.ClosedSetCalls.ShouldBeEmpty();
        Row("App.dll").Status.ShouldBe(FileStatus.Success);
        Row("App.Tests.csproj").Status.ShouldBe(FileStatus.Skipped);
        Logs.ShouldContain(l => l.Message == "Skipping App.Tests: Test project");
    }

    [Fact]
    public async Task Run_ClosedSetFailure_MarksIncludedErrorLeavesSkipped()
    {
        _engine.ClosedSet = (_, _, _, _, _) => Task.FromResult(ClosedSetResult.Failed("closed-set failed"));
        AddFile("first.dll");
        AddFile("second.dll");
        AddSkippedFile("App.Tests.csproj", "Test project");

        var snapshot = await RunAsync();

        Row("first.dll").Status.ShouldBe(FileStatus.Error);
        Row("second.dll").Status.ShouldBe(FileStatus.Error);
        Row("App.Tests.csproj").Status.ShouldBe(FileStatus.Skipped);
        Logs.ShouldContain(l => l.Level == LogLevel.Error && l.Message.Contains("closed-set failed"));
        snapshot.Message.ShouldBe("Completed with errors");
        snapshot.Failed.ShouldBe(2);
    }

    [Fact]
    public async Task Run_ClosedSetSuccess_LoadFailureAndSourceFile_DoNotMarkSuccess()
    {
        var firstPath = Path.Combine(_tempDirectory, "first.dll");
        var secondPath = Path.Combine(_tempDirectory, "second.dll");
        _engine.ClosedSet = (_, _, _, _, _) => Task.FromResult(ClosedSetResult.Succeeded(
            [Ok(input: firstPath, output: firstPath)],
            [new ClosedSetLoadFailure { Path = secondPath, Message = "Bad IL" }]));
        AddFile("first.dll");
        AddFile("second.dll");
        AddFile("Extra.cs");

        var snapshot = await RunAsync();

        Row("first.dll").Status.ShouldBe(FileStatus.Success);
        var failed = Row("second.dll");
        failed.Status.ShouldBe(FileStatus.Error);
        failed.Error.ShouldNotBeNullOrWhiteSpace();
        var source = Row("Extra.cs");
        source.Status.ShouldBe(FileStatus.Skipped);
        source.SkipReason!.ShouldContain("Source files");
        _engine.ObfuscateCalls.ShouldBeEmpty();
        snapshot.Failed.ShouldBe(1);
    }

    [Fact]
    public async Task Run_PreservePublicApi_PassesForcePreservePublicTrue()
    {
        _protection.UpdateSetting("symbolRenaming.preservePublicApi", true);
        AddFile("first.dll");
        AddFile("second.dll");

        await RunAsync();

        var call = _engine.ClosedSetCalls.ShouldHaveSingleItem();
        call.ForcePreservePublic.ShouldBeTrue();
        call.Settings.SymbolRenaming.PreservePublicApi.ShouldBeTrue();
    }

    [Fact]
    public async Task Run_EmptyOutputDirectory_UsesObfyOutNotInputFolder()
    {
        AddFile("first.dll");
        AddFile("second.dll");
        _input.SetOutputFolder("");

        await RunAsync();

        var call = _engine.ClosedSetCalls.ShouldHaveSingleItem();
        call.Output.ShouldEndWith("obfy-out", Case.Insensitive);
        string.Equals(call.Output, _tempDirectory, StringComparison.OrdinalIgnoreCase).ShouldBeFalse();
        Logs.ShouldContain(l => l.Message == $"Output directory: {call.Output}");
    }

    [Fact]
    public async Task Run_ChosenOutputFolder_IsTheClosedSetFolder_AndPerFileOutputGoesThere()
    {
        var folder = Path.Combine(_tempDirectory, "out");
        _input.SetOutputFolder(folder);
        AddFile("first.dll");
        AddFile("second.dll");

        await RunAsync();

        _engine.ClosedSetCalls.ShouldHaveSingleItem().Output.ShouldBe(folder);
        Logs.ShouldNotContain(l => l.Message.StartsWith("Output directory:"));

        var other = new RunCoordinatorTests();
        try
        {
            other._input.SetOutputFolder(folder);
            other.AddFile("only.dll");
            await other.RunAsync();
            other._engine.ObfuscateCalls.ShouldHaveSingleItem().Output.ShouldBe(Path.Combine(folder, "only.obfuscated.dll"));
        }
        finally
        {
            other.Dispose();
        }
    }

    // ---- Sources ----

    [Fact]
    public async Task Run_TwoSourceFiles_AreOneCompilation()
    {
        AddFile("a.cs");
        AddFile("b.cs");

        var snapshot = await RunAsync();

        snapshot.Mode.ShouldBe(RunMode.Sources);
        _engine.SourcesCalls.ShouldHaveSingleItem().Inputs.Count.ShouldBe(2);
        _engine.ObfuscateCalls.ShouldBeEmpty();
        Rows.ShouldAllBe(r => r.Status == FileStatus.Success);
        Logs.ShouldContain(l => l.Message == "Processing 2 source files as one compilation...");
    }

    [Fact]
    public async Task Run_SourcesFailure_MarksEverySourceError()
    {
        _engine.Sources = (_, _, _, _) => Task.FromResult(ObfuscationResult.Failed("does not compile"));
        AddFile("a.cs");
        AddFile("b.cs");

        var snapshot = await RunAsync();

        Rows.ShouldAllBe(r => r.Status == FileStatus.Error && r.Error == "does not compile");
        snapshot.Failed.ShouldBe(2);
    }

    // ---- A second run ----

    [Fact]
    public async Task Run_Twice_StartsFromCleanStatus_AndClearsTheFailureCount()
    {
        var fail = true;
        _engine.Obfuscate = (_, _, _, _) => Task.FromResult(fail ? ObfuscationResult.Failed("boom") : Ok());
        AddFile();

        (await RunAsync()).Failed.ShouldBe(1);
        fail = false;
        var second = await RunAsync();

        second.Failed.ShouldBe(0);
        second.Message.ShouldBe("Obfuscation complete");
        Rows[0].Status.ShouldBe(FileStatus.Success);
        Rows[0].Error.ShouldBeNull();
    }

    // ---- Endpoints ----

    [Fact]
    public async Task Handlers_GetStartAndCancel_ReachTheCoordinator()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _engine.Obfuscate = async (_, _, _, ct) =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return Ok();
        };
        AddFile();

        (await new GetRunRequestHandler(_run).Handle(new GetRunRequest(), default)).CanStart.ShouldBeTrue();

        var started1 = await new StartRunRequestHandler(_run).Handle(new StartRunRequest(), default);
        started1.Status.ShouldBe(RunStatus.Running);
        await started.Task.WaitAsync(Guard);

        var cancelled = await new CancelRunRequestHandler(_run).Handle(new CancelRunRequest(), default);
        cancelled.Message.ShouldBe("Cancelling...");
        await _run.Completion.WaitAsync(Guard);
        _run.GetSnapshot().Status.ShouldBe(RunStatus.Cancelled);
    }

    [Fact]
    public async Task Handler_Start_WhenBlocked_ThrowsTheReason()
    {
        var ex = await Should.ThrowAsync<ObfyApiException>(async () =>
            await new StartRunRequestHandler(_run).Handle(new StartRunRequest(), default));

        ex.Code.ShouldBe("run.blocked");
        ex.Message.ShouldBe("Add at least one included file.");
    }

    // ---- Input is refused during a run ----

    [Fact]
    public async Task Input_IsRefusedWhileARunGoes_AndAllowedWhenIdle()
    {
        var existing = AddFile("first.dll");
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource<ObfuscationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _engine.Obfuscate = (_, _, _, _) =>
        {
            started.TrySetResult();
            return finish.Task;
        };
        var dialogs = new FakeDialogs { Files = [Path.Combine(_tempDirectory, "picked.dll")] };
        File.WriteAllBytes(dialogs.Files[0], []);
        var dropped = Path.Combine(_tempDirectory, "dropped.dll");
        File.WriteAllBytes(dropped, []);
        var intake = new InputIntake(_input, _protection, _run, NullLogger<InputIntake>.Instance);
        var rowId = Rows[0].Id;

        var run = _run.Start();
        await started.Task.WaitAsync(Guard);
        _run.IsRunning.ShouldBeTrue();

        // A native drop has no page caller: it is refused with a notice, and nothing joins the list.
        await new DropPathsRequestHandler(intake).Handle(new DropPathsRequest([dropped]), default);
        Rows.Select(r => r.Path).ShouldBe([existing]);
        _input.GetSnapshot().Notices.ShouldContain(n => n.Level == NoticeLevel.Warning && n.Message == RunCoordinator.InputBlockedMessage);

        // Everything else that changes the list or the configuration is an error the caller sees; the picker is not even opened.
        async Task Blocked(Func<Task> call) =>
            (await Should.ThrowAsync<ObfyApiException>(call)).Code.ShouldBe("run.blocked");

        await Blocked(async () => await new PickFilesRequestHandler(intake, dialogs, _input, _run).Handle(new PickFilesRequest(), default));
        dialogs.FilePicks.ShouldBe(0);
        await Blocked(async () => await new RemoveFileRequestHandler(_input, _run).Handle(new RemoveFileRequest(rowId), default));
        await Blocked(async () => await new ClearFilesRequestHandler(_input, _run).Handle(new ClearFilesRequest(), default));
        await Blocked(async () => await new SetIncludedRequestHandler(_input, _run).Handle(new SetIncludedRequest(rowId, false), default));
        await Blocked(async () => await new OpenConfigRequestHandler(_protection, dialogs, _run).Handle(new OpenConfigRequest(), default));
        await Blocked(async () => await new LoadConfigRequestHandler(_protection, _run).Handle(new LoadConfigRequest(Path.Combine(_tempDirectory, "x.json")), default));
        Rows.Select(r => r.Path).ShouldBe([existing]);
        Rows[0].Included.ShouldBeTrue();

        finish.SetResult(Ok());
        await run.WaitAsync(Guard);
        _run.IsRunning.ShouldBeFalse();

        // Idle again: the same calls go through.
        await new DropPathsRequestHandler(intake).Handle(new DropPathsRequest([dropped]), default);
        Rows.Select(r => r.Path).ShouldBe([existing, dropped], ignoreOrder: true);
        await new PickFilesRequestHandler(intake, dialogs, _input, _run).Handle(new PickFilesRequest(), default);
        dialogs.FilePicks.ShouldBe(1);
        Rows.Count.ShouldBe(3);
        await new SetIncludedRequestHandler(_input, _run).Handle(new SetIncludedRequest(rowId, false), default);
        Rows.Single(r => r.Id == rowId).Included.ShouldBeFalse();
        await new RemoveFileRequestHandler(_input, _run).Handle(new RemoveFileRequest(rowId), default);
        await new ClearFilesRequestHandler(_input, _run).Handle(new ClearFilesRequest(), default);
        Rows.ShouldBeEmpty();
    }

    // ---- A cleanup that throws must not leave the coordinator Running ----

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Run_WhenTheCleanupAfterACancelOrFailureThrows_StillFinishes_AndTheNextStartWorks(bool cancel)
    {
        AddFile();
        _engine.Obfuscate = (_, _, _, _) =>
        {
            // From here every push made while the run is still going fails, as a broken publisher would: the file rows
            // and the results store are written during the cleanup. Finish publishes after the status has changed.
            _events.OnPublish = () =>
            {
                if (_run.IsRunning)
                    throw new InvalidOperationException("publish failed");
            };
            throw cancel ? new OperationCanceledException() : new InvalidOperationException("engine failed");
        };

        var run = _run.Start();
        await run.WaitAsync(Guard);

        var snapshot = _run.GetSnapshot();
        snapshot.Status.ShouldBe(cancel ? RunStatus.Cancelled : RunStatus.Faulted);
        snapshot.CanStart.ShouldBeTrue();
        _run.IsRunning.ShouldBeFalse();

        _events.OnPublish = null;
        _engine.Obfuscate = (_, _, _, _) => Task.FromResult(Ok());
        var after = await RunAsync();
        after.Status.ShouldBe(RunStatus.Done);
    }

    // ---- Fakes ----

    private sealed class FakeEngine : IObfuscationService
    {
        public record ObfuscateCall(string Input, string? Output);
        public record SourcesCall(IReadOnlyList<string> Inputs, string? Output);
        public record MergeCall(IReadOnlyList<string> Inputs, string Output);
        public record ClosedSetCall(IReadOnlyList<ClosedSetInput> Inputs, string Output, ObfySettings Settings, bool ForcePreservePublic);
        public record MapWrite(Dictionary<string, string> Map, string Path);

        private readonly object _gate = new();
        private readonly List<ObfuscateCall> _obfuscateCalls = [];
        private readonly List<SourcesCall> _sourcesCalls = [];
        private readonly List<MergeCall> _mergeCalls = [];
        private readonly List<ClosedSetCall> _closedSetCalls = [];
        private readonly List<MapWrite> _writtenMaps = [];

        public Func<string, string?, ObfySettings, CancellationToken, Task<ObfuscationResult>> Obfuscate { get; set; } =
            (_, _, _, _) => Task.FromResult(ObfuscationResult.Successful(new ObfuscationStatistics()));

        public Func<IReadOnlyList<string>, string?, ObfySettings, CancellationToken, Task<ObfuscationResult>> Sources { get; set; } =
            (_, _, _, _) => Task.FromResult(ObfuscationResult.Successful(new ObfuscationStatistics()));

        public Func<IReadOnlyList<string>, string, ObfySettings, CancellationToken, Task<ObfuscationResult>> Merge { get; set; } =
            (_, _, _, _) => Task.FromResult(ObfuscationResult.Successful(new ObfuscationStatistics()));

        /// <summary>By default every input succeeds with itself as the output, as the real set does.</summary>
        public Func<IReadOnlyList<ClosedSetInput>, string, ObfySettings, bool, CancellationToken, Task<ClosedSetResult>> ClosedSet { get; set; } =
            (inputs, _, _, _, _) => Task.FromResult(ClosedSetResult.Succeeded(inputs
                .Select(i => ObfuscationResult.Successful(new ObfuscationStatistics(), inputPath: i.AssemblyPath, outputPath: i.AssemblyPath))
                .ToList()));

        public Func<Dictionary<string, string>, string, Task> WriteMap { get; set; } = (_, _) => Task.CompletedTask;

        public IReadOnlyList<ObfuscateCall> ObfuscateCalls { get { lock (_gate) return _obfuscateCalls.ToList(); } }
        public IReadOnlyList<SourcesCall> SourcesCalls { get { lock (_gate) return _sourcesCalls.ToList(); } }
        public IReadOnlyList<MergeCall> MergeCalls { get { lock (_gate) return _mergeCalls.ToList(); } }
        public IReadOnlyList<ClosedSetCall> ClosedSetCalls { get { lock (_gate) return _closedSetCalls.ToList(); } }
        public IReadOnlyList<MapWrite> WrittenMaps { get { lock (_gate) return _writtenMaps.ToList(); } }

        public Task<ObfuscationResult> ObfuscateAsync(string inputPath, string? outputPath, ObfySettings settings, CancellationToken cancellationToken = default)
        {
            lock (_gate)
                _obfuscateCalls.Add(new ObfuscateCall(inputPath, outputPath));
            return Obfuscate(inputPath, outputPath, settings, cancellationToken);
        }

        public Task<ObfuscationResult> ObfuscateSourcesAsync(IReadOnlyList<string> inputPaths, string? outputDirectory, ObfySettings settings, CancellationToken cancellationToken = default)
        {
            lock (_gate)
                _sourcesCalls.Add(new SourcesCall(inputPaths, outputDirectory));
            return Sources(inputPaths, outputDirectory, settings, cancellationToken);
        }

        public Task<ObfuscationResult> MergeAndObfuscateAsync(IEnumerable<string> inputPaths, string outputPath, ObfySettings settings, CancellationToken cancellationToken = default)
        {
            var inputs = inputPaths.ToList();
            lock (_gate)
                _mergeCalls.Add(new MergeCall(inputs, outputPath));
            return Merge(inputs, outputPath, settings, cancellationToken);
        }

        public Task<ClosedSetResult> ObfuscateClosedSetAsync(IReadOnlyList<ClosedSetInput> inputs, string outputDirectory, ObfySettings settings, bool forcePreservePublic = false, CancellationToken cancellationToken = default)
        {
            lock (_gate)
                _closedSetCalls.Add(new ClosedSetCall(inputs, outputDirectory, settings, forcePreservePublic));
            return ClosedSet(inputs, outputDirectory, settings, forcePreservePublic, cancellationToken);
        }

        public Task WriteSymbolMapAsync(Dictionary<string, string> symbolMap, string outputPath)
        {
            lock (_gate)
                _writtenMaps.Add(new MapWrite(symbolMap, outputPath));
            return WriteMap(symbolMap, outputPath);
        }

        public Task<IReadOnlyList<ObfuscationResult>> ObfuscateBatchAsync(IEnumerable<string> inputPaths, string outputDirectory, ObfySettings settings, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeReportBuilder : IReportService
    {
        private readonly List<ObfuscationResult> _captured = [];

        public int BuildCalls => Captured.Count;

        public IReadOnlyList<ObfuscationResult> Captured
        {
            get
            {
                lock (_captured)
                    return _captured.ToList();
            }
        }

        public Exception? Failure { get; set; }

        public ObfuscationReport BuildReport(ObfuscationResult result, ObfySettings settings)
        {
            if (Failure is not null)
                throw Failure;
            lock (_captured)
                _captured.Add(result);
            return new ObfuscationReport();
        }

        public ObfuscationReport BuildReport(PipelineContext context, ObfuscationResult result, ObfySettings settings) => throw new NotSupportedException();

        public ObfuscationReport BuildCombinedReport(IReadOnlyList<ObfuscationResult> results, ObfySettings settings) => throw new NotSupportedException();

        public Task GenerateReportAsync(ObfuscationReport report, string outputPath, ReportFormat format, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public string GetFileExtension(ReportFormat format) => ".html";
    }
}
