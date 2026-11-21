using Microsoft.Extensions.Logging.Abstractions;
using Obfy.App.Features.Output;
using Obfy.App.Sessions;
using Obfy.App.Tests.Common;
using Obfy.Core.Models;
using Obfy.Core.Services;
using Shouldly;
using ProtectionSession = Obfy.App.Sessions.ProtectionSession;

namespace Obfy.App.Tests.Features;

/// <summary>
/// The output card is locked on the page during a run, but the host decides: every <c>output.*</c> call that changes
/// the folder or the symbol map fails with <c>run.blocked</c> while a run goes, as the file calls do, so the folder, the
/// hint and the map path the page shows cannot drift from the values the run captured.
/// </summary>
public sealed class OutputHandlersRunBlockedTests : IDisposable
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(10);

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"OutputBlocked_{Guid.NewGuid():N}");
    private readonly FakeSettings _settings = new();
    private readonly FakeDialogs _dialogs = new();
    private readonly GatedEngine _engine = new();
    private readonly InputSession _input;
    private readonly RunCoordinator _run;

    public OutputHandlersRunBlockedTests()
    {
        Directory.CreateDirectory(_root);
        _input = new InputSession(_settings, null, new FakeShell(), null, NullLogger<InputSession>.Instance, TimeSpan.Zero);
        _run = new RunCoordinator(_engine, null!, new ProtectionSession(), _input, new LogBuffer(), new ResultsStore());
    }

    public void Dispose()
    {
        _engine.Finish.TrySetResult(ObfuscationResult.Failed("done"));
        _run.Dispose();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }

    private async Task<Task> StartRunAsync()
    {
        var dll = Path.Combine(_root, "app.dll");
        File.WriteAllBytes(dll, []);
        _input.AddPaths([dll]);
        _input.SetWriteSymbolMap(true);
        var run = _run.Start();
        await _engine.Started.Task.WaitAsync(Guard);
        _run.IsRunning.ShouldBeTrue();
        return run;
    }

    private static async Task Blocked(Func<Task> call) =>
        (await Should.ThrowAsync<ObfyApiException>(call)).Code.ShouldBe("run.blocked");

    [Fact]
    public async Task OutputCalls_AreRefusedWhileARunGoes_AndTheSessionIsUntouched()
    {
        _dialogs.Folder = Path.Combine(_root, "picked");
        _dialogs.SavePath = Path.Combine(_root, "picked-map.json");
        var run = await StartRunAsync();
        var before = _input.GetSnapshot();

        await Blocked(async () => await new SetOutputRequestHandler(_input, _run).Handle(new SetOutputRequest(Path.Combine(_root, "typed")), default));
        await Blocked(async () => await new SetSymbolMapRequestHandler(_input, _run).Handle(new SetSymbolMapRequest(Enabled: false), default));
        await Blocked(async () => await new SetSymbolMapRequestHandler(_input, _run).Handle(new SetSymbolMapRequest(Path: "m.json"), default));
        await Blocked(async () => await new PickOutputFolderRequestHandler(_input, _dialogs, _run).Handle(new PickOutputFolderRequest(), default));
        await Blocked(async () => await new PickSymbolMapRequestHandler(_input, _dialogs, _run).Handle(new PickSymbolMapRequest(), default));

        // The pickers are not even opened.
        _dialogs.FolderPicks.ShouldBe(0);
        _dialogs.SavePicks.ShouldBe(0);
        var after = _input.GetSnapshot();
        after.OutputFolder.ShouldBe(before.OutputFolder);
        after.OutputHint.ShouldBe(before.OutputHint);
        after.WriteSymbolMap.ShouldBeTrue();
        after.SymbolMapPath.ShouldBe(before.SymbolMapPath);
        _settings.OutputAssignments.ShouldBeEmpty();

        _engine.Finish.SetResult(ObfuscationResult.Failed("done"));
        await run.WaitAsync(Guard);
    }

    [Fact]
    public async Task OutputCalls_GoThroughWhenIdle()
    {
        _dialogs.Folder = Path.Combine(_root, "picked");
        _dialogs.SavePath = Path.Combine(_root, "picked-map.json");

        await new SetOutputRequestHandler(_input, _run).Handle(new SetOutputRequest(Path.Combine(_root, "typed")), default);
        _input.GetSnapshot().OutputFolder.ShouldBe(Path.Combine(_root, "typed"));

        await new SetSymbolMapRequestHandler(_input, _run).Handle(new SetSymbolMapRequest(Enabled: true, Path: "m.json"), default);
        _input.GetSnapshot().WriteSymbolMap.ShouldBeTrue();
        _input.GetSnapshot().SymbolMapPath.ShouldBe("m.json");

        await new PickOutputFolderRequestHandler(_input, _dialogs, _run).Handle(new PickOutputFolderRequest(), default);
        _input.GetSnapshot().OutputFolder.ShouldBe(Path.Combine(_root, "picked"));

        await new PickSymbolMapRequestHandler(_input, _dialogs, _run).Handle(new PickSymbolMapRequest(), default);
        _input.GetSnapshot().SymbolMapPath.ShouldBe(Path.Combine(_root, "picked-map.json"));
    }

    [Fact]
    public async Task SetSymbolMap_WithAnEmptyPath_ResetsTheDefault_ThroughTheHandler()
    {
        var handler = new SetSymbolMapRequestHandler(_input, _run);
        await handler.Handle(new SetSymbolMapRequest(Enabled: true, Path: "m.json"), default);

        var snapshot = await handler.Handle(new SetSymbolMapRequest(Path: ""), default);

        snapshot.SymbolMapPath.ShouldBeEmpty();
    }

    /// <summary>Holds the first file until the test lets it go.</summary>
    private sealed class GatedEngine : IObfuscationService
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<ObfuscationResult> Finish { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<ObfuscationResult> ObfuscateAsync(string inputPath, string? outputPath, ObfySettings settings, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            return Finish.Task;
        }

        public Task<IReadOnlyList<ObfuscationResult>> ObfuscateBatchAsync(IEnumerable<string> inputPaths, string outputDirectory, ObfySettings settings, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ObfuscationResult> ObfuscateSourcesAsync(IReadOnlyList<string> inputPaths, string? outputDirectory, ObfySettings settings, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task WriteSymbolMapAsync(Dictionary<string, string> symbolMap, string outputPath) => Task.CompletedTask;

        public Task<ObfuscationResult> MergeAndObfuscateAsync(IEnumerable<string> inputPaths, string outputPath, ObfySettings settings, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Obfy.Core.Models.Solution.ClosedSetResult> ObfuscateClosedSetAsync(IReadOnlyList<Obfy.Core.Models.Solution.ClosedSetInput> inputs, string outputDirectory, ObfySettings settings, bool forcePreservePublic = false, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
