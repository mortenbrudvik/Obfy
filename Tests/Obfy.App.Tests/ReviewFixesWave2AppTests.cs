using Microsoft.Extensions.Logging.Abstractions;
using Obfy.App.Features.App;
using Obfy.App.Features.Files;
using Obfy.App.Features.Output;
using Obfy.App.Features.Preview;
using Obfy.App.Features.Results;
using Obfy.App.Features.Settings;
using Obfy.App.Models;
using Obfy.App.Services;
using Obfy.App.Sessions;
using Obfy.App.Tests.Common;
using Obfy.Core.Models;
using Obfy.Core.Models.Solution;
using Shouldly;
using LogLevel = Obfy.App.Models.LogLevel;
using ProtectionSession = Obfy.App.Sessions.ProtectionSession;

namespace Obfy.App.Tests;

public sealed class ReviewFixesWave2AppTests : IDisposable
{
    private readonly ReviewHarness _h = new();

    public void Dispose() => _h.Dispose();

    [Fact]
    public void LogBuffer_CapsAtMaxLines_PreservingWarningsAndErrorsOverInfoAndDebug()
    {
        LogBuffer.DefaultMaxLines.ShouldBe(5000);

        using var buffer = new LogBuffer(_h.Events, TimeSpan.FromHours(1), maxLines: 5);
        buffer.Add(LogLevel.Error, "err-1");
        buffer.Add(LogLevel.Debug, "dbg-1");
        buffer.Add(LogLevel.Warning, "warn-1");
        buffer.Add(LogLevel.Info, "info-1");
        buffer.Add(LogLevel.Info, "info-2");
        buffer.Add(LogLevel.Debug, "dbg-2");
        buffer.Add(LogLevel.Info, "info-3");

        var lines = buffer.Get();
        lines.Count.ShouldBe(5);
        lines.Select(l => l.Message).ShouldBe(["err-1", "warn-1", "info-2", "dbg-2", "info-3"]);

        // Once all remaining slots hold warnings/errors, incoming info lines must not evict them,
        // while newer warnings/errors evict the oldest warning/error.
        buffer.Add(LogLevel.Error, "err-2");
        buffer.Add(LogLevel.Warning, "warn-2");
        buffer.Add(LogLevel.Error, "err-3");
        buffer.Add(LogLevel.Info, "info-4");
        buffer.Get().Select(l => l.Message).ShouldBe(["err-1", "warn-1", "err-2", "warn-2", "err-3"]);

        buffer.Add(LogLevel.Error, "err-4");
        buffer.Get().Select(l => l.Message).ShouldBe(["warn-1", "err-2", "warn-2", "err-3", "err-4"]);
    }

    [Fact]
    public void LogBuffer_DefaultCap_Enforces5000LinesAndKeepsErrors()
    {
        using var buffer = new LogBuffer(_h.Events, TimeSpan.FromHours(1));
        buffer.Add(LogLevel.Error, "critical-error");
        buffer.Add(LogLevel.Warning, "important-warning");
        for (var i = 0; i < 5100; i++)
            buffer.Add(LogLevel.Info, $"line-{i}");

        var lines = buffer.Get();
        lines.Count.ShouldBe(5000);
        lines.ShouldContain(l => l.Message == "critical-error");
        lines.ShouldContain(l => l.Message == "important-warning");
    }

    [Fact]
    public async Task ConfigFile_UnknownKeys_ProduceKeyOnlyNoticesInOpenConfigAndInputIntake()
    {
        var secretFolder = Path.Combine(_h.Directory, "secret-customer-path");
        Directory.CreateDirectory(secretFolder);
        var configPath = Path.Combine(secretFolder, "obfy.json");
        await File.WriteAllTextAsync(configPath, """
            {
              "level": "custom",
              "controlFlow": { "enabled": true, "intensty": 55 },
              "bogusFlag": true
            }
            """);

        var loaded = await ConfigFile.LoadWithUnknownKeysAsync(configPath);
        loaded.Settings.ControlFlow.Enabled.ShouldBeTrue();
        loaded.UnknownKeys.ShouldBe(["controlFlow.intensty", "bogusFlag"], ignoreOrder: true);

        // OpenConfigRequest adds a notice with only key names, never the file path.
        var dialogs = new BlockingDialogs { FilesResult = [configPath] };
        dialogs.Release();
        await new OpenConfigRequestHandler(_h.Protection, dialogs, _h.Run, _h.Input)
            .Handle(new OpenConfigRequest(), default);

        var openNotice = _h.Input.GetSnapshot().Notices.ShouldHaveSingleItem();
        openNotice.Level.ShouldBe(NoticeLevel.Warning);
        openNotice.Message.ShouldContain("controlFlow.intensty");
        openNotice.Message.ShouldContain("bogusFlag");
        openNotice.Message.ShouldNotContain("secret-customer-path");
        openNotice.Message.ShouldNotContain("obfy.json");

        // Dropped .json via InputIntake adds the same key-only notice.
        var input2 = new InputSession(new FakeSettings(), null, new FakeShell(), _h.Events, NullLogger<InputSession>.Instance, TimeSpan.Zero);
        var protection2 = new ProtectionSession(_h.Events);
        using var run2 = new RunCoordinator(_h.Engine, new StubReports(), protection2, input2, _h.Log, _h.Results, _h.Events);
        var intake = new InputIntake(input2, protection2, run2, NullLogger<InputIntake>.Instance);

        await intake.AddPathsAsync([configPath], default);

        var dropNotice = input2.GetSnapshot().Notices.ShouldHaveSingleItem();
        dropNotice.Level.ShouldBe(NoticeLevel.Warning);
        dropNotice.Message.ShouldContain("controlFlow.intensty");
        dropNotice.Message.ShouldContain("bogusFlag");
        dropNotice.Message.ShouldNotContain("secret-customer-path");
    }

    [Fact]
    public async Task ConfigFile_SaveAsync_HandlesInvalidPathAndDuplicateKeysInPreserveExisting()
    {
        var invalidEx = await Should.ThrowAsync<ObfyApiException>(async () =>
            await ConfigFile.SaveAsync(new ObfySettings(), Path.Combine(_h.Directory, "bad\0name.json")));
        invalidEx.Code.ShouldBe("settings.configNotSaved");

        var duplicatePath = Path.Combine(_h.Directory, "dup.json");
        await File.WriteAllTextAsync(duplicatePath, """{ "level": "Standard", "level": "Minimal" }""");
        var settings = ObfySettings.ForLevel(ObfuscationLevel.Aggressive);

        await Should.NotThrowAsync(async () =>
            await ConfigFile.SaveAsync(settings, duplicatePath, preserveExisting: true));
        (await ConfigFile.LoadAsync(duplicatePath)).Level.ShouldBe(ObfuscationLevel.Aggressive);
    }

    [Fact]
    public void OutputCollisions_RejectsAlternateDataStreamOnSymbolMap_AndNormalizesExtendedUncPrefix()
    {
        var input = _h.AddFile("App.dll");
        var plan = _h.Input.PlanOutput(merge: false);

        if (OperatingSystem.IsWindows())
        {
            var adsPath = input + "::$DATA";
            var adsCollision = OutputCollisions.Find([input], plan, defaultPlan: null, symbolMapPath: adsPath);
            adsCollision.ShouldNotBeNull();
            adsCollision.Message.ShouldContain("alternate data stream");
        }

        var uncListed = @"\\?\UNC\server\share\out\App.dll";
        var uncOutput = @"\\server\share\out\App.dll";
        var uncPlan = new OutputPlan(OutputMode.Each, @"\\server\share\out", null, "hint", [new PlannedOutput(input, uncOutput)]);
        var uncCollision = OutputCollisions.Find([uncListed], uncPlan, defaultPlan: null, probe: new NoopProbe());
        uncCollision.ShouldNotBeNull();
    }

    [Fact]
    public async Task ResultsStore_GetPreview_AtomicValidationAndCancellationDuringRunReset()
    {
        var path = typeof(ReviewFixesWave2AppTests).Assembly.Location;
        var decompiles = 0;
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var store = new ResultsStore(_h.Events, new ScriptedDecompiler(_ =>
        {
            Interlocked.Increment(ref decompiles);
            entered.Set();
            release.Wait(ReviewHarness.Guard);
            return "class Preview {}";
        }));

        store.SetPreviewPaths([path]);
        var pending = new GetPreviewRequestHandler(store).Handle(new GetPreviewRequest(path), default).AsTask();
        entered.Wait(ReviewHarness.Guard).ShouldBeTrue();

        // A new run resets the store mid-decompile.
        store.BeginRun();
        release.Set();
        await pending;

        store.GetSnapshot().HasPreview.ShouldBeFalse();
        store.GetSnapshot().SelectedPreviewPath.ShouldBeNull();
        store.GetPreview().Text.ShouldBeEmpty();

        // Now path is no longer known for the new run; must reject without invoking the decompiler.
        var before = decompiles;
        var ex = await Should.ThrowAsync<ObfyApiException>(async () =>
            await new GetPreviewRequestHandler(store).Handle(new GetPreviewRequest(path), default));
        ex.Code.ShouldBe("preview.unknownPath");
        decompiles.ShouldBe(before);
    }

    [Fact]
    public async Task SettingsService_ConcurrentSaves_NeverCollideOnTempFile()
    {
        var prefsPath = Path.Combine(_h.Directory, "prefs", "ui-preferences.json");
        var logger = new CapturingLogger<SettingsService>();
        var service = new SettingsService(logger, prefsPath);

        var saves = Enumerable.Range(0, 20).Select(i => Task.Run(async () =>
        {
            service.LastOutputDirectory = $@"C:\out\{i}";
            service.GenerateSymbolMap = i % 2 == 0;
            await service.SavePreferencesAsync();
        }));

        await Task.WhenAll(saves);

        logger.Entries.ShouldBeEmpty();
        Directory.GetFiles(Path.GetDirectoryName(prefsPath)!).ShouldBe([prefsPath]);

        var reader = new SettingsService(logger, prefsPath);
        await reader.LoadPreferencesAsync();
        reader.LastOutputDirectory.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task ReportPageErrorHandler_LogsSanitizedScreenAndTypeWithoutMessageOrPath()
    {
        var logger = new CapturingLogger<ReportPageErrorRequestHandler>();
        var handler = new ReportPageErrorRequestHandler(logger);

        await handler.Handle(
            new ReportPageErrorRequest("protection\r\nC:\\Users\\alice\\secret", new string('X', 120)),
            default);

        var entry = logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(Microsoft.Extensions.Logging.LogLevel.Error);
        entry.Exception.ShouldBeNull();
        entry.Message.ShouldNotContain("\r");
        entry.Message.ShouldNotContain("\n");
        entry.Message.ShouldNotContain("\\");
        entry.Message.ShouldNotContain(new string('X', 65));

        await handler.Handle(new ReportPageErrorRequest(null, "  "), default);
        logger.Entries.Count.ShouldBe(2);
        logger.Entries[1].Message.ShouldContain("unknown");
    }

    [Fact]
    public async Task PickSymbolMap_ExportMap_AndUpdateSettings_ValidateBeforeSideEffects()
    {
        var dialogs = new BlockingDialogs();
        dialogs.Release();

        // PickSymbolMap refuses before opening the save dialog when WriteSymbolMap is off.
        var mapEx = await Should.ThrowAsync<ObfyApiException>(async () =>
            await new PickSymbolMapRequestHandler(_h.Input, dialogs, _h.Run).Handle(new PickSymbolMapRequest(), default));
        mapEx.Code.ShouldBe("output.symbolMapOff");
        dialogs.SavePicks.ShouldBe(0);

        // ExportMap refuses before opening the save dialog when there is no symbol map.
        var exportEx = await Should.ThrowAsync<ObfyApiException>(async () =>
            await new ExportMapRequestHandler(_h.Results, dialogs).Handle(new ExportMapRequest(), default));
        exportEx.Code.ShouldBe("results.noSymbolMap");
        dialogs.SavePicks.ShouldBe(0);

        // ExportMap captures the symbol map before the dialog opens so a concurrent Clear doesn't wipe it.
        await _h.Results.LoadResultsAsync(new Dictionary<string, string> { ["Foo"] = "a" }, new ObfuscationStatistics(), TimeSpan.Zero);
        var exportDialogs = new BlockingDialogs { SaveResult = Path.Combine(_h.Directory, "exported.json") };
        var exportTask = new ExportMapRequestHandler(_h.Results, exportDialogs).Handle(new ExportMapRequest(), default).AsTask();
        await exportDialogs.Entered.Task.WaitAsync(ReviewHarness.Guard);
        _h.Results.Clear();
        exportDialogs.Release();
        var exported = await exportTask;
        exported.Exported.ShouldBeTrue();
        (await File.ReadAllTextAsync(exportDialogs.SaveResult!)).ShouldContain("Foo");

        // UpdateSettings rejects null Patch with settings.invalidPatch instead of NullReferenceException.
        var patchEx = await Should.ThrowAsync<ObfyApiException>(async () =>
            await new UpdateSettingsRequestHandler(_h.Protection, _h.Run).Handle(new UpdateSettingsRequest(null!), default));
        patchEx.Code.ShouldBe("settings.invalidPatch");
    }

    [Fact]
    public async Task DialogHandlers_RejectWithRunBlocked_WhenRunStartsWhileDialogIsPending()
    {
        var dll = _h.AddFile("app.dll");
        var extraDll = Path.Combine(_h.Directory, "extra.dll");
        File.WriteAllBytes(extraDll, []);
        var cfg = Path.Combine(_h.Directory, "cfg.json");
        await File.WriteAllTextAsync(cfg, """{ "level": "aggressive" }""");

        var runStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runFinish = new TaskCompletionSource<ObfuscationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _h.Engine.Obfuscate = (_, _, _, _) =>
        {
            runStarted.TrySetResult();
            return runFinish.Task;
        };

        // Hold PickOutputFolder dialog open, start a run, then complete the dialog.
        var dialogs = new BlockingDialogs { FolderResult = Path.Combine(_h.Directory, "picked-out") };
        var folderTask = new PickOutputFolderRequestHandler(_h.Input, dialogs, _h.Run).Handle(new PickOutputFolderRequest(), default).AsTask();
        await dialogs.Entered.Task.WaitAsync(ReviewHarness.Guard);

        var runTask = _h.Run.Start();
        await runStarted.Task.WaitAsync(ReviewHarness.Guard);
        dialogs.Release();

        (await Should.ThrowAsync<ObfyApiException>(folderTask)).Code.ShouldBe("run.blocked");
        _h.Input.GetSnapshot().OutputFolder.ShouldBeEmpty();

        runFinish.SetResult(ObfuscationResult.Successful(new ObfuscationStatistics()));
        await runTask.WaitAsync(ReviewHarness.Guard);

        // Verify PickSymbolMap, OpenConfig, PickSigningKey, and PickFiles also reject mid-run TOCTOU.
        _h.Input.SetWriteSymbolMap(true);
        await AssertBlockedMidDialog(
            d => new PickSymbolMapRequestHandler(_h.Input, d, _h.Run).Handle(new PickSymbolMapRequest(), default).AsTask(),
            new BlockingDialogs { SaveResult = Path.Combine(_h.Directory, "map.json") });
        _h.Input.GetSnapshot().SymbolMapPath.ShouldBeEmpty();

        await AssertBlockedMidDialog(
            d => new OpenConfigRequestHandler(_h.Protection, d, _h.Run, _h.Input).Handle(new OpenConfigRequest(), default).AsTask(),
            new BlockingDialogs { FilesResult = [cfg] });
        _h.Protection.ConfigPath.ShouldBeNull();

        await AssertBlockedMidDialog(
            d => new PickSigningKeyRequestHandler(_h.Protection, d, _h.Run).Handle(new PickSigningKeyRequest(), default).AsTask(),
            new BlockingDialogs { KeyResult = @"C:\keys\test.snk" });
        _h.Protection.ToObfySettings().Signing.KeyFile.ShouldBeNull();

        var intake = new InputIntake(_h.Input, _h.Protection, _h.Run, NullLogger<InputIntake>.Instance);
        await AssertBlockedMidDialog(
            d => new PickFilesRequestHandler(intake, d, _h.Input, _h.Run).Handle(new PickFilesRequest(), default).AsTask(),
            new BlockingDialogs { FilesResult = [extraDll] });
        _h.Input.GetSnapshot().Files.Select(f => f.Path).ShouldBe([dll]);
    }

    [Fact]
    public async Task ClosedSet_DuplicateFilenamesAcrossDirectories_DoNotCrossMatchMissingResult()
    {
        var dir1 = Path.Combine(_h.Directory, "net6");
        var dir2 = Path.Combine(_h.Directory, "net8");
        Directory.CreateDirectory(dir1);
        Directory.CreateDirectory(dir2);
        var a1 = Path.Combine(dir1, "App.dll");
        var a2 = Path.Combine(dir2, "App.dll");
        File.WriteAllBytes(a1, []);
        File.WriteAllBytes(a2, []);
        _h.Input.AddPaths([a1, a2]);

        _h.Engine.ClosedSet = _ => ClosedSetResult.Succeeded(
        [
            ObfuscationResult.Successful(new ObfuscationStatistics(), inputPath: a1, outputPath: Path.Combine(_h.Directory, "out", "net6", "App.dll"))
        ]);

        await _h.Run.Start().WaitAsync(ReviewHarness.Guard);

        var runSnapshot = _h.Run.GetSnapshot();
        runSnapshot.Failed.ShouldBe(1);

        var rows = _h.Input.GetSnapshot().Files;
        rows.Single(r => r.Path == a1).Status.ShouldBe(FileStatus.Success);
        rows.Single(r => r.Path == a2).Status.ShouldBe(FileStatus.Error);
    }

    [Fact]
    public async Task SettingsService_SavePreferencesAsync_DoesNotDeadlockGateWhenPathFails()
    {
        var badPrefsPath = Path.Combine(_h.Directory, "bad\0path", "ui-preferences.json");
        var logger = new CapturingLogger<SettingsService>();
        var service = new SettingsService(logger, badPrefsPath);

        await service.SavePreferencesAsync().WaitAsync(ReviewHarness.Guard);
        await service.SavePreferencesAsync().WaitAsync(ReviewHarness.Guard);

        logger.Entries.Count.ShouldBe(2);
    }

    [Fact]
    public async Task ConfigLoad_ReplacesAndClearsUnknownKeysNoticeWithoutStackingOrDroppingUnrelatedNotices()
    {
        var firstBad = Path.Combine(_h.Directory, "first-bad.json");
        var secondBad = Path.Combine(_h.Directory, "second-bad.json");
        var clean = Path.Combine(_h.Directory, "clean.json");
        await File.WriteAllTextAsync(firstBad, """{ "level": "standard", "unknownOne": true }""");
        await File.WriteAllTextAsync(secondBad, """{ "level": "standard", "unknownTwo": true }""");
        await File.WriteAllTextAsync(clean, """{ "level": "aggressive" }""");

        _h.Input.AddNotice(NoticeLevel.Warning, "Unrelated", "Keep this notice.");

        var d1 = new BlockingDialogs { FilesResult = [firstBad] };
        d1.Release();
        await new OpenConfigRequestHandler(_h.Protection, d1, _h.Run, _h.Input).Handle(new OpenConfigRequest(), default);

        var notices1 = _h.Input.GetSnapshot().Notices;
        notices1.Count.ShouldBe(2);
        notices1.ShouldContain(n => n.Title == "Unrelated");
        notices1.Single(n => n.Title == "Unknown configuration keys").Message.ShouldContain("unknownOne");

        var d2 = new BlockingDialogs { FilesResult = [secondBad] };
        d2.Release();
        await new OpenConfigRequestHandler(_h.Protection, d2, _h.Run, _h.Input).Handle(new OpenConfigRequest(), default);

        var notices2 = _h.Input.GetSnapshot().Notices;
        notices2.Count.ShouldBe(2);
        notices2.ShouldContain(n => n.Title == "Unrelated");
        var replaced = notices2.Single(n => n.Title == "Unknown configuration keys");
        replaced.Message.ShouldContain("unknownTwo");
        replaced.Message.ShouldNotContain("unknownOne");

        var d3 = new BlockingDialogs { FilesResult = [clean] };
        d3.Release();
        await new OpenConfigRequestHandler(_h.Protection, d3, _h.Run, _h.Input).Handle(new OpenConfigRequest(), default);

        var notices3 = _h.Input.GetSnapshot().Notices;
        var remaining = notices3.ShouldHaveSingleItem();
        remaining.Title.ShouldBe("Unrelated");
    }

    [Fact]
    public void LogBuffer_DoesNotPushImmediatelyEvictedInfoLinesInPendingBatch()
    {
        var events = new FakeEvents();
        using var buffer = new LogBuffer(events, TimeSpan.FromHours(1), maxLines: 2);
        buffer.Add(LogLevel.Error, "err-1");
        buffer.Add(LogLevel.Error, "err-2");
        buffer.Flush();

        var countAfterErrors = events.Published.Count;

        // Buffer is full of errors; incoming Info is immediately evicted and must not appear in log.appended.
        buffer.Add(LogLevel.Info, "evicted-info");
        buffer.Flush();

        events.Published.Count.ShouldBe(countAfterErrors);
        buffer.Get().Select(l => l.Message).ShouldBe(["err-1", "err-2"]);

        // A new Error evicts err-1 and does appear in log.appended.
        buffer.Add(LogLevel.Error, "err-3");
        buffer.Flush();

        events.Published.Count.ShouldBe(countAfterErrors + 1);
        var published = events.Published[^1];
        var batch = published.Data.ShouldBeOfType<LogAppended>();
        batch.Lines.Select(l => l.Message).ShouldBe(["err-3"]);
        buffer.Get().Select(l => l.Message).ShouldBe(["err-2", "err-3"]);
    }

    private async Task AssertBlockedMidDialog<T>(Func<BlockingDialogs, Task<T>> invoke, BlockingDialogs dialogs)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource<ObfuscationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _h.Engine.Obfuscate = (_, _, _, _) =>
        {
            started.TrySetResult();
            return finish.Task;
        };

        var call = invoke(dialogs);
        await dialogs.Entered.Task.WaitAsync(ReviewHarness.Guard);

        var runTask = _h.Run.Start();
        await started.Task.WaitAsync(ReviewHarness.Guard);
        dialogs.Release();

        (await Should.ThrowAsync<ObfyApiException>(call)).Code.ShouldBe("run.blocked");

        finish.SetResult(ObfuscationResult.Successful(new ObfuscationStatistics()));
        await runTask.WaitAsync(ReviewHarness.Guard);
    }

    private sealed class BlockingDialogs : IFileDialogs
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string[] FilesResult { get; set; } = [];
        public string? FolderResult { get; set; }
        public string? SaveResult { get; set; }
        public string? KeyResult { get; set; }
        public int SavePicks { get; private set; }

        public void Release() => _gate.TrySetResult();

        public async Task<string[]> PickFilesAsync(string title, string filter, bool multi)
        {
            Entered.TrySetResult();
            await _gate.Task;
            return FilesResult;
        }

        public async Task<string?> PickFolderAsync(string title)
        {
            Entered.TrySetResult();
            await _gate.Task;
            return FolderResult;
        }

        public async Task<string?> PickSaveAsync(string title, string filter, string? defaultName)
        {
            SavePicks++;
            Entered.TrySetResult();
            await _gate.Task;
            return SaveResult;
        }

        public async Task<string?> PickKeyAsync()
        {
            Entered.TrySetResult();
            await _gate.Task;
            return KeyResult;
        }
    }

    private sealed class ScriptedDecompiler(Func<string, string> decompile) : IAssemblyDecompiler
    {
        public string Decompile(string assemblyPath) => decompile(assemblyPath);
    }

    private sealed class NoopProbe : IPathProbe
    {
        public bool FileExists(string path) => false;
        public bool DirectoryExists(string path) => false;
        public FileSystemInfo? ResolveLinkTarget(string path) => null;
    }
}
