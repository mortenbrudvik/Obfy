using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Autofac;
using Microsoft.Extensions.Logging;
using Obfy.Core.Models;
using Obfy.Core.Models.Solution;
using Obfy.Core.Pipeline;
using Obfy.Core.Services.Solution;
using Obfy.Core.Utilities;

namespace Obfy.Core.Services;

/// <summary>
/// Default implementation of the obfuscation service.
/// </summary>
public class ObfuscationService : IObfuscationService
{
    private readonly IAssemblyProcessor _assemblyProcessor;
    private readonly ISourceProcessor _sourceProcessor;
    private readonly IObfuscationPipeline _pipeline;
    private readonly IAssemblyMerger _assemblyMerger;
    private readonly ILogger<ObfuscationService> _logger;
    private readonly IClosedSetProcessor _closedSetProcessor;
    private readonly ILifetimeScope? _lifetimeScope;

    public ObfuscationService(
        IAssemblyProcessor assemblyProcessor,
        ISourceProcessor sourceProcessor,
        IObfuscationPipeline pipeline,
        IAssemblyMerger assemblyMerger,
        ILogger<ObfuscationService> logger,
        IClosedSetProcessor closedSetProcessor,
        ILifetimeScope? lifetimeScope = null)
    {
        _assemblyProcessor = assemblyProcessor;
        _sourceProcessor = sourceProcessor;
        _pipeline = pipeline;
        _assemblyMerger = assemblyMerger;
        _logger = logger;
        _closedSetProcessor = closedSetProcessor;
        _lifetimeScope = lifetimeScope;
    }

    /// <inheritdoc/>
    public Task<ObfuscationResult> ObfuscateAsync(
        string inputPath,
        string? outputPath,
        ObfySettings settings,
        CancellationToken cancellationToken = default) =>
        ObfuscateAsync(inputPath, outputPath, settings, ObfuscationRunOptions.Default, cancellationToken);

    /// <inheritdoc/>
    public async Task<ObfuscationResult> ObfuscateAsync(
        string inputPath,
        string? outputPath,
        ObfySettings settings,
        ObfuscationRunOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        // The log keeps Debug and above: no path, no exception text. Counts, stages and exception types only.
        _logger.LogInformation("Starting obfuscation run");

        var isDirectory = Directory.Exists(inputPath);
        if (!File.Exists(inputPath) && !isDirectory)
        {
            return ObfuscationResult.Failed($"Input file not found: {inputPath}");
        }

        var target = ObfuscationTarget.ForFile(inputPath, outputPath, isDirectory);

        var prepared = TryPrepareSettings(settings, out var settingsFailure);
        if (prepared is null)
            return settingsFailure!;

        settings = prepared;
        var gatingWarnings = ApplyRuntimeProfileGating(settings, target.TargetType);

        // One cache key per run, from the settings the pipeline will actually use (after profile gating) and
        // the input as it is before the run starts. The same key serves the hit check and the write.
        var effectiveOutput = outputPath ?? GenerateOutputPath(inputPath);
        string? cacheKey = null;
        ObfySettings? keySettings = null;
        string? cacheNote = null;
        if (settings.Incremental.Enabled && !isDirectory && target.TargetType == TargetType.Assembly &&
            IsSameFile(inputPath, effectiveOutput))
        {
            // The run replaces its own input. A key from the input could not match the file this run leaves behind,
            // and a stale cache that matches the file now on disk would "reuse" an unprotected input. No key, no
            // hit check, no cache file.
            cacheNote = InPlaceCacheNote;
            _logger.LogInformation("Incremental cache not used: the output replaces the input");
        }
        else if (settings.Incremental.Enabled && !isDirectory && target.TargetType == TargetType.Assembly)
        {
            keySettings = settings.Clone();
            try
            {
                cacheKey = IncrementalCache.ComputeKey(inputPath, keySettings);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Unreadable input: the load below reports it. This run just does not use the cache.
                _logger.LogWarning("Incremental cache key could not be computed ({ExceptionType})", ex.GetType().Name);
            }
        }

        if (cacheKey is not null)
        {
            var cached = IncrementalCache.TryReadHit(effectiveOutput, keySettings!, cacheKey);
            if (cached is not null && options.NeedsSymbolMap && cached.SymbolMapEntries > 0)
            {
                // The caller will read the symbol map and a hit has none to give: the cache never stores it, because
                // it lists the original names. Do the work again rather than hand back an empty map as if it were
                // the map of this run. (A cached run that renamed nothing has an empty map, so a hit is exact.)
                _logger.LogInformation("Incremental cache hit not used: the caller needs the symbol map");
                cacheNote = SymbolMapNeededNote;
            }
            else if (cached is not null)
            {
                _logger.LogInformation("Incremental cache hit");
                return ReuseCachedOutput(inputPath, effectiveOutput, settings, gatingWarnings, cached);
            }
        }

        PipelineContext context;

        try
        {
            context = target.TargetType switch
            {
                TargetType.Assembly => await _assemblyProcessor.LoadAsync(inputPath, settings, cancellationToken).ConfigureAwait(false),
                TargetType.SourceCode => await _sourceProcessor.LoadAsync(inputPath, settings, cancellationToken).ConfigureAwait(false),
                _ => throw new ArgumentException($"Unsupported target type: {target.TargetType}")
            };

            context.InputPath = inputPath;
            context.OutputPath = target.EffectiveOutputPath;
            context.Warnings.AddRange(gatingWarnings);
            if (cacheNote is not null)
                context.Warnings.Add(cacheNote);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError("Failed to load input ({ExceptionType})", ex.GetType().Name);
            return ObfuscationResult.Failed($"Failed to load input: {ex.Message}", ex);
        }

        if (target.TargetType == TargetType.Assembly &&
            context.Module is not null &&
            ProtectedInputGuard.ContainsInjectedHelpers(context.Module))
        {
            DisposeModule(context);
            _logger.LogWarning("Input already contains Obfy runtime helper types; run refused");
            return ObfuscationResult.Failed(ProtectedInputGuard.Refusal(inputPath));
        }

        ILifetimeScope? runScope = null;
        var pipeline = _pipeline;
        if (_lifetimeScope is not null)
        {
            runScope = _lifetimeScope.BeginLifetimeScope();
            pipeline = runScope.Resolve<IObfuscationPipeline>();
        }

        try
        {
            // Execute the pipeline
            var result = await pipeline.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);

            if (!result.Success)
            {
                return result;
            }

            if (context.Statistics.TotalTransformations == 0)
                context.Warnings.Add(ObfuscationWarnings.NoTransformations);

            // Save the result
            try
            {
                if (target.TargetType == TargetType.Assembly && !IncrementalCache.Invalidate(effectiveOutput))
                {
                    context.Warnings.Add(
                        "A stale incremental cache file could not be removed: delete '"
                        + IncrementalCache.CachePath(effectiveOutput) + "' before relying on the cache again.");
                }

                switch (target.TargetType)
                {
                    case TargetType.Assembly:
                        await _assemblyProcessor.SaveAsync(context, effectiveOutput, cancellationToken).ConfigureAwait(false);
                        break;
                    case TargetType.SourceCode:
                        await _sourceProcessor.SaveAsync(context, effectiveOutput, cancellationToken).ConfigureAwait(false);
                        break;
                }

                _logger.LogInformation("Obfuscation completed; output written");

                if (settings.Packing.Enabled && target.TargetType != TargetType.Assembly)
                {
                    context.Warnings.Add(
                        "Packing skipped: it applies only to assemblies with an entry point, not source.");
                    _logger.LogWarning("Packing enabled but the target is {Type}", target.TargetType);
                }

                string? packedPath = null;
                if (settings.Packing.Enabled && target.TargetType == TargetType.Assembly)
                {
                    try
                    {
                        packedPath = PackingApplicator.Pack(
                            effectiveOutput, settings, inputPath, context.Warnings);
                        _logger.LogInformation("Packed output written");
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogError("Packing failed ({ExceptionType})", ex.GetType().Name);
                        return ObfuscationResult.Failed($"Packing failed: {ex.Message}", ex);
                    }
                }

                if (cacheKey is not null)
                {
                    // Numbers only. The warnings a hit derives again (profile gating, the cache note of this run, the
                    // packing line) are not part of the count.
                    var runRecord = new CachedRun(
                        context.Statistics,
                        context.SymbolMap.Count,
                        context.SkippedItems.Count,
                        Math.Max(
                            0,
                            context.Warnings.Count - gatingWarnings.Count
                                - (cacheNote is null ? 0 : 1) - (packedPath is null ? 0 : 1)));
                    WriteIncrementalCache(inputPath, effectiveOutput, keySettings!, cacheKey, runRecord, context.Warnings);
                }

                return ObfuscationResult.Successful(
                    context.Statistics,
                    inputPath: inputPath,
                    outputPath: effectiveOutput,
                    elapsedTime: result.ElapsedTime,
                    processingTimes: context.ProcessingTimes.ToList(),
                    skippedItems: context.SkippedItems.ToList(),
                    symbolMap: new Dictionary<string, string>(context.SymbolMap),
                    warnings: context.Warnings.ToList(),
                    packedLauncherPath: packedPath);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError("Failed to save obfuscated output ({ExceptionType})", ex.GetType().Name);
                return ObfuscationResult.Failed($"Failed to save output: {ex.Message}", ex);
            }
        }
        finally
        {
            runScope?.Dispose();
            // The loaded module holds native resources. SaveAsync disposes and nulls it on the
            // success path; dispose here too so a pipeline/write failure cannot leak it.
            DisposeModule(context);
        }
    }

    /// <summary>
    /// Clones the caller's settings (so ApplyLevel cannot mutate the UI's, a batch's or a shared config's
    /// object), applies a non-Custom level and validates. Custom is left alone: the CLI and the UI hand over
    /// explicit flags as Custom so a preset cannot overwrite them. Failures come back as a result.
    /// </summary>
    private ObfySettings? TryPrepareSettings(ObfySettings settings, out ObfuscationResult? failure)
    {
        try
        {
            var prepared = settings.Clone();
            if (prepared.Level != ObfuscationLevel.Custom)
                prepared.ApplyLevel();
            prepared.Validate();
            failure = null;
            return prepared;
        }
        catch (ValidationException ex)
        {
            _logger.LogError("Invalid obfuscation settings ({ExceptionType})", ex.GetType().Name);
            failure = ObfuscationResult.Failed($"Invalid settings: {ex.Message}", ex);
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError("Could not prepare obfuscation settings ({ExceptionType})", ex.GetType().Name);
            failure = ObfuscationResult.Failed($"Invalid settings: {ex.Message}", ex);
            return null;
        }
    }

    /// <summary>
    /// Switches off what the runtime profile cannot support, on the working clone, before anything keys or
    /// runs on the settings. The warnings are fixed texts (no user data), so they are safe to log.
    /// </summary>
    private List<string> ApplyRuntimeProfileGating(ObfySettings settings, TargetType targetType)
    {
        var scratch = new PipelineContext { TargetType = targetType, Settings = settings };
        RuntimeProfileGating.Apply(settings, scratch);
        foreach (var warning in scratch.Warnings)
            _logger.LogWarning("{Warning}", warning);

        return scratch.Warnings;
    }

    /// <summary>
    /// Writes the cache for the key computed before the run. The output was built from the inputs as they were
    /// then, so the cache is written only if they are unchanged now.
    /// </summary>
    private void WriteIncrementalCache(
        string inputPath, string output, ObfySettings keySettings, string cacheKey, CachedRun run, List<string> warnings)
    {
        string? current = null;
        try
        {
            current = IncrementalCache.ComputeKey(inputPath, keySettings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning("Incremental cache key could not be re-checked ({ExceptionType})", ex.GetType().Name);
        }

        if (!string.Equals(current, cacheKey, StringComparison.Ordinal))
        {
            warnings.Add(
                "Incremental cache not written: the input or one of its dependencies changed while the run was in progress.");
            return;
        }

        if (!IncrementalCache.TryWrite(output, cacheKey, run))
        {
            _logger.LogWarning("Could not write the incremental cache");
            warnings.Add(
                "Incremental cache could not be written next to the output, so the next run will not be skipped.");
        }
    }

    /// <summary>
    /// The result of a cache hit: the output of the earlier run is the output of this one, and its statistics are
    /// the statistics. What the cache does not keep (symbol map, skipped items, warning text, timings) is empty, and
    /// the warnings say how much of it there was, so a hit never reads as a run that found nothing to report.
    /// </summary>
    private static ObfuscationResult ReuseCachedOutput(
        string inputPath, string output, ObfySettings settings, List<string> gatingWarnings, CachedRun cached)
    {
        string? packed = null;
        var warnings = new List<string> { "Incremental: reused cached output" };
        warnings.AddRange(gatingWarnings);
        if (settings.Packing.Enabled)
        {
            if (settings.Packing.IsPortable)
            {
                packed = ManagedLauncherPacker.LauncherPathFor(output);
                warnings.Add("Packed launcher: " + packed);
            }
            else
            {
                packed = output;
                warnings.Add("Packed native host: " + packed);
            }
        }

        var cacheFile = Path.GetFileName(IncrementalCache.CachePath(output));
        if (cached.SkippedItems > 0)
        {
            warnings.Add(
                $"The run that produced this output skipped {cached.SkippedItems} item(s) and left them unobfuscated. " +
                $"The cache keeps no names, so they are not listed here. Delete '{cacheFile}' and run again to list them.");
        }

        if (cached.Warnings > 0)
        {
            warnings.Add(
                $"The run that produced this output raised {cached.Warnings} warning(s). " +
                $"The cache keeps no warning text, so they are not repeated here. Delete '{cacheFile}' and run again to see them.");
        }

        return ObfuscationResult.Successful(
            cached.Statistics,
            inputPath: inputPath,
            outputPath: output,
            warnings: warnings,
            packedLauncherPath: packed,
            fromCache: true);
    }

    /// <summary>
    /// Said when a cache hit was available but the caller reads the symbol map. Fixed text: it names no file.
    /// </summary>
    internal const string SymbolMapNeededNote =
        "Incremental cache not used for this run: the symbol map is needed and the cache does not store it " +
        "(it lists the original names), so the output was built again.";

    /// <summary>
    /// Said once per in-place run when incremental mode is on. Fixed text: it names no file.
    /// </summary>
    internal const string InPlaceCacheNote =
        "Incremental cache not used: the output replaces the input file. Write the output to a different path to use the cache.";

    /// <summary>
    /// True when both paths name the same file: full paths, compared the way the platform compares file names
    /// (case-insensitively on Windows). An unusable path is not the same file; the run reports it when it saves.
    /// </summary>
    private static bool IsSameFile(string inputPath, string outputPath)
    {
        try
        {
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return string.Equals(Path.GetFullPath(inputPath), Path.GetFullPath(outputPath), comparison);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException)
        {
            return false;
        }
    }

    private static void DisposeModule(PipelineContext context)
    {
        if (context.Module is IDisposable module)
        {
            module.Dispose();
            context.Module = null;
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ObfuscationResult>> ObfuscateBatchAsync(
        IEnumerable<string> inputPaths,
        string outputDirectory,
        ObfySettings settings,
        CancellationToken cancellationToken = default)
    {
        var slots = new List<(ObfuscationResult? Source, int AssemblyIndex)>();
        var pendingSources = new List<string>();
        var assemblies = new List<string>();

        foreach (var inputPath in inputPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsCSharpFile(inputPath))
            {
                pendingSources.Add(inputPath);
                continue;
            }

            await FlushSourcesAsync().ConfigureAwait(false);
            if (IsAssemblyFile(inputPath))
            {
                assemblies.Add(inputPath);
                slots.Add((null, assemblies.Count - 1));
                continue;
            }

            var outputPath = Path.Combine(outputDirectory, Path.GetFileName(inputPath));
            var single = await ObfuscateAsync(inputPath, outputPath, settings, cancellationToken).ConfigureAwait(false);
            slots.Add((single, -1));
        }

        await FlushSourcesAsync().ConfigureAwait(false);

        IReadOnlyList<ObfuscationResult> assemblyResults = [];
        if (assemblies.Count == 1)
        {
            var single = assemblies[0];
            var outputPath = Path.Combine(outputDirectory, Path.GetFileName(single));
            assemblyResults =
            [
                await ObfuscateAsync(single, outputPath, settings, cancellationToken).ConfigureAwait(false)
            ];
        }
        else if (assemblies.Count > 1)
        {
            var closed = await ObfuscateClosedSetAsync(
                assemblies.Select(path => ClosedSetInput.From(path)).ToList(),
                outputDirectory,
                settings,
                forcePreservePublic: false,
                cancellationToken).ConfigureAwait(false);

            if (closed.Success && closed.ModuleResults.Count == assemblies.Count)
            {
                assemblyResults = closed.ModuleResults;
            }
            else
            {
                var message = string.IsNullOrWhiteSpace(closed.ErrorMessage)
                    ? "Closed-set obfuscation failed."
                    : closed.ErrorMessage;
                assemblyResults = assemblies.Select(_ => ObfuscationResult.Failed(message)).ToList();
            }
        }

        var results = new List<ObfuscationResult>(slots.Count);
        foreach (var (source, assemblyIndex) in slots)
            results.Add(source ?? assemblyResults[assemblyIndex]);

        return results;

        async Task FlushSourcesAsync()
        {
            if (pendingSources.Count == 0)
                return;

            if (pendingSources.Count == 1)
            {
                var single = pendingSources[0];
                var outputPath = Path.Combine(outputDirectory, Path.GetFileName(single));
                var result = await ObfuscateAsync(single, outputPath, settings, cancellationToken).ConfigureAwait(false);
                slots.Add((result, -1));
            }
            else
            {
                var result = await ObfuscateSourcesAsync(
                    pendingSources.ToArray(), outputDirectory, settings, cancellationToken).ConfigureAwait(false);
                slots.Add((result, -1));
            }

            pendingSources.Clear();
        }
    }

    /// <inheritdoc/>
    public async Task<ObfuscationResult> ObfuscateSourcesAsync(
        IReadOnlyList<string> inputPaths,
        string? outputDirectory,
        ObfySettings settings,
        CancellationToken cancellationToken = default)
    {
        if (inputPaths == null || inputPaths.Count == 0)
            return ObfuscationResult.Failed("No source files were provided.");

        var prepared = TryPrepareSettings(settings, out var settingsFailure);
        if (prepared is null)
            return settingsFailure!;

        settings = prepared;
        var gatingWarnings = ApplyRuntimeProfileGating(settings, TargetType.SourceCode);

        var output = outputDirectory;
        if (string.IsNullOrWhiteSpace(output))
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(inputPaths[0])) ?? ".";
            output = Path.Combine(directory, "obfy-src");
        }

        PipelineContext context;
        try
        {
            context = await _sourceProcessor.LoadFilesAsync(inputPaths, settings, cancellationToken)
                .ConfigureAwait(false);
            context.OutputPath = output;
            context.Warnings.AddRange(gatingWarnings);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError("Failed to load source files ({ExceptionType})", ex.GetType().Name);
            return ObfuscationResult.Failed($"Failed to load input: {ex.Message}", ex);
        }

        ILifetimeScope? runScope = null;
        var pipeline = _pipeline;
        if (_lifetimeScope is not null)
        {
            runScope = _lifetimeScope.BeginLifetimeScope();
            pipeline = runScope.Resolve<IObfuscationPipeline>();
        }

        try
        {
            var result = await pipeline.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
            if (!result.Success)
                return result;

            if (context.Statistics.TotalTransformations == 0)
                context.Warnings.Add(ObfuscationWarnings.NoTransformations);

            try
            {
                await _sourceProcessor.SaveAsync(context, output, cancellationToken).ConfigureAwait(false);
                if (settings.Packing.Enabled)
                {
                    context.Warnings.Add(
                        "Packing skipped: it applies only to assemblies with an entry point, not source.");
                }

                return ObfuscationResult.Successful(
                    context.Statistics,
                    inputPath: inputPaths[0],
                    outputPath: output,
                    elapsedTime: result.ElapsedTime,
                    processingTimes: context.ProcessingTimes.ToList(),
                    skippedItems: context.SkippedItems.ToList(),
                    symbolMap: new Dictionary<string, string>(context.SymbolMap),
                    warnings: context.Warnings.ToList());
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError("Failed to save obfuscated source ({ExceptionType})", ex.GetType().Name);
                return ObfuscationResult.Failed($"Failed to save output: {ex.Message}", ex);
            }
        }
        finally
        {
            runScope?.Dispose();
            DisposeModule(context);
        }
    }

    private static bool IsCSharpFile(string path) =>
        Path.GetExtension(path).Equals(".cs", StringComparison.OrdinalIgnoreCase);

    private static bool IsAssemblyFile(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".dll", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".exe", StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    public async Task WriteSymbolMapAsync(Dictionary<string, string> symbolMap, string outputPath)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        var json = JsonSerializer.Serialize(symbolMap, options);
        await File.WriteAllTextAsync(outputPath, json).ConfigureAwait(false);

        _logger.LogInformation("Symbol map written with {Count} entries", symbolMap.Count);
    }

    /// <inheritdoc/>
    public Task<ObfuscationResult> MergeAndObfuscateAsync(
        IEnumerable<string> inputPaths,
        string outputPath,
        ObfySettings settings,
        CancellationToken cancellationToken = default) =>
        MergeAndObfuscateAsync(inputPaths, outputPath, settings, ObfuscationRunOptions.Default, cancellationToken);

    /// <inheritdoc/>
    public async Task<ObfuscationResult> MergeAndObfuscateAsync(
        IEnumerable<string> inputPaths,
        string outputPath,
        ObfySettings settings,
        ObfuscationRunOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var inputList = inputPaths.ToList();

        if (inputList.Count < 2)
        {
            return ObfuscationResult.Failed("At least two assemblies are required for merging");
        }

        _logger.LogInformation("Starting merge-and-obfuscate of {Count} assemblies", inputList.Count);

        // Create temp file for merged assembly
        var tempDir = Path.Combine(Path.GetTempPath(), "obfy_merge_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);
        var tempMergedPath = Path.Combine(tempDir, Path.GetFileName(outputPath));

        try
        {
            // Merge assemblies
            var mergeResult = await _assemblyMerger.MergeAsync(
                inputList,
                tempMergedPath,
                settings.AssemblyMerge,
                cancellationToken).ConfigureAwait(false);

            if (!mergeResult.Success)
            {
                _logger.LogError("Assembly merge failed");
                return ObfuscationResult.Failed($"Merge failed: {mergeResult.ErrorMessage}");
            }

            _logger.LogInformation(
                "Merged {Count} assemblies in {Duration:F2}s",
                mergeResult.MergedAssemblyCount,
                mergeResult.Duration.TotalSeconds);

            // Obfuscate the merged assembly
            var obfuscationResult = await ObfuscateAsync(
                tempMergedPath,
                outputPath,
                settings,
                options,
                cancellationToken).ConfigureAwait(false);

            // Add merge info to the result
            if (obfuscationResult.Success)
            {
                _logger.LogInformation(
                    "Successfully merged and obfuscated {Count} assemblies",
                    mergeResult.MergedAssemblyCount);

                var left = LeftOutOfMerge(inputList, mergeResult);
                if (left.Count > 0)
                {
                    obfuscationResult.Warnings.Add(
                        $"Not merged (excluded by assemblyMerge.excludePatterns): {string.Join(", ", left)}. " +
                        "They are not part of the output and must stay next to it.");
                }
            }

            return obfuscationResult;
        }
        finally
        {
            // Cleanup temp directory
            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to clean up the merge temp directory ({ExceptionType})", ex.GetType().Name);
            }
        }
    }

    /// <summary>File names of the inputs the merger dropped (exclude patterns), so the result can say so.</summary>
    private static List<string> LeftOutOfMerge(List<string> inputs, AssemblyMergeResult merge)
    {
        if (merge.MergedAssemblyCount >= inputs.Count)
            return [];

        var merged = new HashSet<string>(merge.MergedAssemblies, StringComparer.OrdinalIgnoreCase);
        return inputs
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrEmpty(name) && !merged.Contains(name))
            .Select(name => name!)
            .ToList();
    }

    /// <inheritdoc/>
    public async Task<ClosedSetResult> ObfuscateClosedSetAsync(
        IReadOnlyList<ClosedSetInput> inputs,
        string outputDirectory,
        ObfySettings settings,
        bool forcePreservePublic = false,
        CancellationToken cancellationToken = default)
    {
        if (_lifetimeScope is null)
        {
            return await _closedSetProcessor.ExecuteAsync(
                inputs,
                outputDirectory,
                settings,
                forcePreservePublic,
                cancellationToken).ConfigureAwait(false);
        }

        using var runScope = _lifetimeScope.BeginLifetimeScope();
        var processor = runScope.Resolve<IClosedSetProcessor>();
        return await processor.ExecuteAsync(
            inputs,
            outputDirectory,
            settings,
            forcePreservePublic,
            cancellationToken).ConfigureAwait(false);
    }

    private static string GenerateOutputPath(string inputPath)
    {
        var directory = Path.GetDirectoryName(inputPath) ?? ".";
        var name = Path.GetFileNameWithoutExtension(inputPath);
        var extension = Path.GetExtension(inputPath);
        return Path.Combine(directory, $"{name}.obfuscated{extension}");
    }
}
