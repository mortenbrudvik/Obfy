using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Obfy.App.Services;
using Obfy.App.Sessions;
using Obfy.Core.Models;
using Obfy.Core.Models.Solution;
using Obfy.Core.Pipeline;
using Obfy.Core.Services;
using Obfy.Core.Services.Reporting;

namespace Obfy.App.Tests.Common;

/// <summary>An event publisher that records what it is given and lets a test hold a publish before it is recorded.</summary>
internal sealed class HookedEvents : IEventPublisher
{
    private readonly List<(string Name, object? Data)> _published = [];

    public IReadOnlyList<(string Name, object? Data)> Published
    {
        get
        {
            lock (_published)
                return _published.ToList();
        }
    }

    /// <summary>Runs on the publishing thread before the event is recorded; may block or throw.</summary>
    public Action<string, object?>? Before { get; set; }

    public void Publish(string eventName, object? data)
    {
        Before?.Invoke(eventName, data);
        lock (_published)
            _published.Add((eventName, data));
    }
}

/// <summary>An <see cref="ILogger{T}"/> that keeps what it was given: level, rendered text and the exception object.</summary>
internal sealed class CapturingLogger<T> : ILogger<T>
{
    public sealed record Entry(Microsoft.Extensions.Logging.LogLevel Level, string Message, Exception? Exception);

    private readonly List<Entry> _entries = [];

    public IReadOnlyList<Entry> Entries
    {
        get
        {
            lock (_entries)
                return _entries.ToList();
        }
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

    public void Log<TState>(
        Microsoft.Extensions.Logging.LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        lock (_entries)
            _entries.Add(new Entry(logLevel, formatter(state, exception), exception));
    }
}

/// <summary>A scriptable engine: every call goes to a delegate a test sets, and defaults to a quick success.</summary>
internal sealed class StubEngine : IObfuscationService
{
    private readonly object _gate = new();
    private readonly List<string> _obfuscated = [];
    private readonly List<IReadOnlyList<string>> _closedSetInputs = [];
    private readonly List<string> _writtenMaps = [];

    public Func<string, string?, ObfySettings, CancellationToken, Task<ObfuscationResult>> Obfuscate { get; set; } =
        (_, _, _, _) => Task.FromResult(ObfuscationResult.Successful(new ObfuscationStatistics()));

    public Func<IReadOnlyList<ClosedSetInput>, ClosedSetResult> ClosedSet { get; set; } =
        inputs => ClosedSetResult.Succeeded(inputs
            .Select(i => ObfuscationResult.Successful(new ObfuscationStatistics(), inputPath: i.AssemblyPath, outputPath: i.AssemblyPath))
            .ToList());

    public Func<Dictionary<string, string>, string, Task> WriteMap { get; set; } = (_, _) => Task.CompletedTask;

    public IReadOnlyList<string> ObfuscatedInputs { get { lock (_gate) return _obfuscated.ToList(); } }
    public IReadOnlyList<IReadOnlyList<string>> ClosedSetInputs { get { lock (_gate) return _closedSetInputs.ToList(); } }
    public IReadOnlyList<string> WrittenMaps { get { lock (_gate) return _writtenMaps.ToList(); } }

    public Task<ObfuscationResult> ObfuscateAsync(string inputPath, string? outputPath, ObfySettings settings, CancellationToken cancellationToken = default)
    {
        lock (_gate)
            _obfuscated.Add(inputPath);
        return Obfuscate(inputPath, outputPath, settings, cancellationToken);
    }

    public Task<ClosedSetResult> ObfuscateClosedSetAsync(IReadOnlyList<ClosedSetInput> inputs, string outputDirectory, ObfySettings settings, bool forcePreservePublic = false, CancellationToken cancellationToken = default)
    {
        lock (_gate)
            _closedSetInputs.Add(inputs.Select(i => i.AssemblyPath).ToList());
        return Task.FromResult(ClosedSet(inputs));
    }

    public Task WriteSymbolMapAsync(Dictionary<string, string> symbolMap, string outputPath)
    {
        lock (_gate)
            _writtenMaps.Add(outputPath);
        return WriteMap(symbolMap, outputPath);
    }

    public Task<IReadOnlyList<ObfuscationResult>> ObfuscateBatchAsync(IEnumerable<string> inputPaths, string outputDirectory, ObfySettings settings, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<ObfuscationResult> ObfuscateSourcesAsync(IReadOnlyList<string> inputPaths, string? outputDirectory, ObfySettings settings, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<ObfuscationResult> MergeAndObfuscateAsync(IEnumerable<string> inputPaths, string outputPath, ObfySettings settings, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}

internal sealed class StubReports : IReportService
{
    public ObfuscationReport BuildReport(ObfuscationResult result, ObfySettings settings) => new();

    public ObfuscationReport BuildReport(PipelineContext context, ObfuscationResult result, ObfySettings settings) => throw new NotSupportedException();

    public ObfuscationReport BuildCombinedReport(IReadOnlyList<ObfuscationResult> results, ObfySettings settings) => throw new NotSupportedException();

    public Task GenerateReportAsync(ObfuscationReport report, string outputPath, ReportFormat format, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public string GetFileExtension(ReportFormat format) => ".html";
}

/// <summary>The sessions and a real <see cref="RunCoordinator"/> over <see cref="StubEngine"/>, for the review regression tests.</summary>
internal sealed class ReviewHarness : IDisposable
{
    public static readonly TimeSpan Guard = TimeSpan.FromSeconds(10);

    public HookedEvents Events { get; } = new();
    public StubEngine Engine { get; } = new();
    public CapturingLogger<RunCoordinator> Logger { get; } = new();
    public Obfy.App.Sessions.ProtectionSession Protection { get; }
    public InputSession Input { get; }
    public LogBuffer Log { get; }
    public ResultsStore Results { get; }
    public RunCoordinator Run { get; }
    public string Directory { get; } = Path.Combine(Path.GetTempPath(), $"ReviewHarness_{Guid.NewGuid():N}");

    public ReviewHarness(Func<string, bool>? keyFileExists = null, IPathProbe? pathProbe = null)
    {
        System.IO.Directory.CreateDirectory(Directory);
        Protection = new Obfy.App.Sessions.ProtectionSession(Events, keyFileExists);
        Input = new InputSession(new FakeSettings(), new FakeAnalyzer(), new FakeShell(), Events, NullLogger<InputSession>.Instance, TimeSpan.Zero, pathProbe);
        Log = new LogBuffer(Events, TimeSpan.FromHours(1));
        Results = new ResultsStore(Events);
        Run = new RunCoordinator(Engine, new StubReports(), Protection, Input, Log, Results, Events, Logger);
    }

    public string AddFile(string name = "input.dll")
    {
        var path = Path.Combine(Directory, name);
        File.WriteAllBytes(path, []);
        Input.AddPaths([path]);
        return path;
    }

    public void Dispose()
    {
        Run.Dispose();
        Log.Dispose();
        try
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
