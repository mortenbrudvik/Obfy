using Microsoft.Extensions.Logging.Abstractions;
using Obfy.App.Models;
using Obfy.App.Sessions;
using Obfy.App.Tests.Common;
using Obfy.Core.Models;
using Obfy.Core.Services;
using Shouldly;
using ProtectionSession = Obfy.App.Sessions.ProtectionSession;

namespace Obfy.App.Tests.Sessions;

/// <summary>
/// A chosen output folder: the hint promises <c>name.obfuscated.ext</c>, so the run must write exactly that. Writing
/// <c>name.ext</c> there put the output on top of the input whenever the folder was the input's own.
/// </summary>
public sealed class InputSessionOutputNamingTests : IDisposable
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(10);

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"InputNaming_{Guid.NewGuid():N}");
    private readonly string _inputDir;
    private readonly RecordingEngine _engine = new();
    private readonly InputSession _input;
    private readonly RunCoordinator _run;

    public InputSessionOutputNamingTests()
    {
        _inputDir = Path.Combine(_root, "in");
        Directory.CreateDirectory(_inputDir);
        _input = new InputSession(new FakeSettings(), null, new FakeShell(), null, NullLogger<InputSession>.Instance, TimeSpan.Zero);
        _run = new RunCoordinator(_engine, null!, new ProtectionSession(), _input, new LogBuffer(), new ResultsStore());
    }

    public void Dispose()
    {
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

    private string AddFile(string name)
    {
        var path = Path.Combine(_inputDir, name);
        File.WriteAllBytes(path, []);
        _input.AddPaths([path]);
        return path;
    }

    private async Task RunAsync() => await _run.Start().WaitAsync(Guard);

    [Fact]
    public async Task Each_WithAChosenFolder_WritesTheNameTheHintPromises()
    {
        var input = AddFile("app.dll");
        var folder = Path.Combine(_root, "out");
        _input.SetOutputFolder(folder);

        var hint = _input.GetSnapshot().OutputHint;
        await RunAsync();

        var call = _engine.ObfuscateCalls.ShouldHaveSingleItem();
        call.Input.ShouldBe(input);
        call.Output.ShouldBe(Path.Combine(folder, "app.obfuscated.dll"));
        hint.ShouldContain("app.obfuscated.dll");
        hint.ShouldContain(folder);
    }

    [Fact]
    public async Task Each_WhenTheChosenFolderIsTheInputsOwn_NeverWritesOverTheInput()
    {
        var input = AddFile("app.dll");
        _input.SetOutputFolder(_inputDir);

        await RunAsync();

        var output = _engine.ObfuscateCalls.ShouldHaveSingleItem().Output.ShouldNotBeNull();
        Path.GetFullPath(output).Equals(Path.GetFullPath(input), StringComparison.OrdinalIgnoreCase).ShouldBeFalse();
        output.ShouldBe(Path.Combine(_inputDir, "app.obfuscated.dll"));
    }

    [Fact]
    public async Task Each_ManyFiles_EveryOutputIsTheirObfuscatedName_AndNeverTheInput()
    {
        // One assembly and one source: not a closed set, so each is written alone.
        var dll = AddFile("lib.dll");
        var cs = AddFile("Program.cs");
        _input.SetOutputFolder(_inputDir);

        await RunAsync();

        var outputs = _engine.ObfuscateCalls.ToDictionary(c => c.Input, c => c.Output);
        outputs[dll].ShouldBe(Path.Combine(_inputDir, "lib.obfuscated.dll"));
        outputs[cs].ShouldBe(Path.Combine(_inputDir, "Program.obfuscated.cs"));
    }

    [Fact]
    public async Task Each_WithoutAChosenFolder_LeavesTheNameToTheEngine()
    {
        AddFile("app.dll");

        await RunAsync();

        _engine.ObfuscateCalls.ShouldHaveSingleItem().Output.ShouldBeNull();
        _input.GetSnapshot().OutputHint.ShouldBe("Writes app.obfuscated.dll next to the input.");
    }

    [Fact]
    public void Merge_WithAChosenFolder_IsAnObfuscatedNameInThatFolder()
    {
        AddFile("first.dll");
        AddFile("second.dll");
        var folder = Path.Combine(_root, "merged");
        _input.SetOutputFolder(folder);

        var plan = _input.PlanOutput(merge: true);

        plan.MergePath.ShouldBe(Path.Combine(folder, "first.obfuscated.dll"));
    }

    [Fact]
    public void Each_TwoSources_HintSaysTheyAreCompiledTogether_NotWrittenOneByOne()
    {
        // Two C# files are one compilation the engine writes to a folder; without a chosen folder that is obfy-src
        // next to the first file. The old hint claimed name.obfuscated.ext next to each input.
        AddFile("a.cs");
        AddFile("b.cs");

        var hint = _input.GetSnapshot().OutputHint;

        hint.ShouldContain("obfy-src");
        hint.ShouldNotContain("name.obfuscated.ext");

        var folder = Path.Combine(_root, "out");
        _input.SetOutputFolder(folder);
        _input.GetSnapshot().OutputHint.ShouldContain(folder);
    }

    /// <summary>Records what the run asks for and fails every file, so no report is built.</summary>
    private sealed class RecordingEngine : IObfuscationService
    {
        public record ObfuscateCall(string Input, string? Output);

        private readonly object _gate = new();
        private readonly List<ObfuscateCall> _calls = [];

        public IReadOnlyList<ObfuscateCall> ObfuscateCalls
        {
            get
            {
                lock (_gate)
                    return _calls.ToList();
            }
        }

        public Task<ObfuscationResult> ObfuscateAsync(string inputPath, string? outputPath, ObfySettings settings, CancellationToken cancellationToken = default)
        {
            lock (_gate)
                _calls.Add(new ObfuscateCall(inputPath, outputPath));
            return Task.FromResult(ObfuscationResult.Failed("recorded"));
        }

        public Task<IReadOnlyList<ObfuscationResult>> ObfuscateBatchAsync(IEnumerable<string> inputPaths, string outputDirectory, ObfySettings settings, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ObfuscationResult> ObfuscateSourcesAsync(IReadOnlyList<string> inputPaths, string? outputDirectory, ObfySettings settings, CancellationToken cancellationToken = default) =>
            Task.FromResult(ObfuscationResult.Failed("recorded"));

        public Task WriteSymbolMapAsync(Dictionary<string, string> symbolMap, string outputPath) => Task.CompletedTask;

        public Task<ObfuscationResult> MergeAndObfuscateAsync(IEnumerable<string> inputPaths, string outputPath, ObfySettings settings, CancellationToken cancellationToken = default) =>
            Task.FromResult(ObfuscationResult.Failed("recorded"));

        public Task<Obfy.Core.Models.Solution.ClosedSetResult> ObfuscateClosedSetAsync(IReadOnlyList<Obfy.Core.Models.Solution.ClosedSetInput> inputs, string outputDirectory, ObfySettings settings, bool forcePreservePublic = false, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
