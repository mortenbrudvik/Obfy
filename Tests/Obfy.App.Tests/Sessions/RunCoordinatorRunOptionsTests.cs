using Microsoft.Extensions.Logging.Abstractions;
using Obfy.App.Sessions;
using Obfy.App.Tests.Common;
using Obfy.Core.Models;
using Obfy.Core.Services;
using Shouldly;
using ProtectionSession = Obfy.App.Sessions.ProtectionSession;

namespace Obfy.App.Tests.Sessions;

/// <summary>
/// The Results screen lists the symbols of every run, so a run tells the engine it reads the symbol map. Without
/// that, an incremental-cache hit would hand the page an empty list (the cache never stores the map: it holds the
/// original names).
/// </summary>
public sealed class RunCoordinatorRunOptionsTests : IDisposable
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(10);

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"RunOptions_{Guid.NewGuid():N}");
    private readonly OptionsRecordingEngine _engine = new();
    private readonly ProtectionSession _protection = new();
    private readonly InputSession _input;
    private readonly RunCoordinator _run;

    public RunCoordinatorRunOptionsTests()
    {
        Directory.CreateDirectory(_root);
        _input = new InputSession(new FakeSettings(), null, new FakeShell(), null, NullLogger<InputSession>.Instance, TimeSpan.Zero);
        _run = new RunCoordinator(_engine, null!, _protection, _input, new LogBuffer(), new ResultsStore());
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

    private void AddFile(string name)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllBytes(path, []);
        _input.AddPaths([path]);
    }

    [Fact]
    public async Task EachFile_IsRunWithTheSymbolMapNeeded()
    {
        AddFile("app.dll");

        await _run.Start().WaitAsync(Guard);

        _engine.ObfuscateOptions.ShouldHaveSingleItem().NeedsSymbolMap.ShouldBeTrue();
    }

    [Fact]
    public async Task Merge_IsRunWithTheSymbolMapNeeded()
    {
        AddFile("first.dll");
        AddFile("second.dll");
        _protection.UpdateSetting("assemblyMerge.enabled", true);

        await _run.Start().WaitAsync(Guard);

        _engine.MergeOptions.ShouldHaveSingleItem().NeedsSymbolMap.ShouldBeTrue();
        _engine.ObfuscateOptions.ShouldBeEmpty();
    }

    /// <summary>
    /// Records the options of each call and fails the run, so no report is built. The calls without options throw: a
    /// run that stopped passing them would not reach the assertions.
    /// </summary>
    private sealed class OptionsRecordingEngine : IObfuscationService
    {
        private readonly object _gate = new();
        private readonly List<ObfuscationRunOptions> _obfuscate = [];
        private readonly List<ObfuscationRunOptions> _merge = [];

        public IReadOnlyList<ObfuscationRunOptions> ObfuscateOptions
        {
            get
            {
                lock (_gate)
                    return _obfuscate.ToList();
            }
        }

        public IReadOnlyList<ObfuscationRunOptions> MergeOptions
        {
            get
            {
                lock (_gate)
                    return _merge.ToList();
            }
        }

        public Task<ObfuscationResult> ObfuscateAsync(string inputPath, string? outputPath, ObfySettings settings, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The run must pass its options.");

        public Task<ObfuscationResult> ObfuscateAsync(string inputPath, string? outputPath, ObfySettings settings, ObfuscationRunOptions options, CancellationToken cancellationToken = default)
        {
            lock (_gate)
                _obfuscate.Add(options);
            return Task.FromResult(ObfuscationResult.Failed("recorded"));
        }

        public Task<ObfuscationResult> MergeAndObfuscateAsync(IEnumerable<string> inputPaths, string outputPath, ObfySettings settings, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The run must pass its options.");

        public Task<ObfuscationResult> MergeAndObfuscateAsync(IEnumerable<string> inputPaths, string outputPath, ObfySettings settings, ObfuscationRunOptions options, CancellationToken cancellationToken = default)
        {
            lock (_gate)
                _merge.Add(options);
            return Task.FromResult(ObfuscationResult.Failed("recorded"));
        }

        public Task<IReadOnlyList<ObfuscationResult>> ObfuscateBatchAsync(IEnumerable<string> inputPaths, string outputDirectory, ObfySettings settings, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ObfuscationResult> ObfuscateSourcesAsync(IReadOnlyList<string> inputPaths, string? outputDirectory, ObfySettings settings, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task WriteSymbolMapAsync(Dictionary<string, string> symbolMap, string outputPath) => Task.CompletedTask;

        public Task<Obfy.Core.Models.Solution.ClosedSetResult> ObfuscateClosedSetAsync(IReadOnlyList<Obfy.Core.Models.Solution.ClosedSetInput> inputs, string outputDirectory, ObfySettings settings, bool forcePreservePublic = false, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
