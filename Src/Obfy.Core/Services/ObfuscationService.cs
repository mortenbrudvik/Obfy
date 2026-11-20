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
    public async Task<ObfuscationResult> ObfuscateAsync(
        string inputPath,
        string? outputPath,
        ObfySettings settings,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting obfuscation of {InputPath}", inputPath);

        var isDirectory = Directory.Exists(inputPath);
        if (!File.Exists(inputPath) && !isDirectory)
        {
            return ObfuscationResult.Failed($"Input file not found: {inputPath}");
        }

        var target = ObfuscationTarget.ForFile(inputPath, outputPath, isDirectory);

        // Clone so ApplyLevel cannot mutate the caller's object (UI, batch, or a shared config).
        settings = settings.Clone();

        if (settings.Level != ObfuscationLevel.Custom)
        {
            settings.ApplyLevel();
        }

        try
        {
            settings.Validate();
        }
        catch (ValidationException ex)
        {
            _logger.LogError(ex, "Invalid obfuscation settings");
            return ObfuscationResult.Failed($"Invalid settings: {ex.Message}", ex);
        }

        var effectiveOutputEarly = outputPath ?? GenerateOutputPath(inputPath);
        if (settings.Incremental.Enabled &&
            !isDirectory &&
            target.TargetType == TargetType.Assembly &&
            IncrementalCache.TryHit(inputPath, effectiveOutputEarly, settings))
        {
            _logger.LogInformation("Incremental cache hit for {InputPath}", inputPath);
            string? packed = null;
            var warnings = new List<string> { "Incremental: reused cached output" };
            if (settings.Packing.Enabled)
            {
                if (settings.Packing.IsPortable)
                {
                    packed = ManagedLauncherPacker.LauncherPathFor(effectiveOutputEarly);
                    warnings.Add("Packed launcher: " + packed);
                }
                else
                {
                    packed = effectiveOutputEarly;
                    warnings.Add("Packed native host: " + packed);
                }
            }

            return ObfuscationResult.Successful(
                new ObfuscationStatistics(),
                inputPath: inputPath,
                outputPath: effectiveOutputEarly,
                warnings: warnings,
                packedLauncherPath: packed);
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
            RuntimeProfileGating.Apply(settings, context);
            foreach (var warning in context.Warnings)
                _logger.LogWarning("{Warning}", warning);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to load {InputPath}", inputPath);
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
            // Execute the pipeline
            var result = await pipeline.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);

            if (!result.Success)
            {
                return result;
            }

            // Save the result
            try
            {
                var effectiveOutput = outputPath ?? GenerateOutputPath(inputPath);

                switch (target.TargetType)
                {
                    case TargetType.Assembly:
                        await _assemblyProcessor.SaveAsync(context, effectiveOutput, cancellationToken).ConfigureAwait(false);
                        break;
                    case TargetType.SourceCode:
                        await _sourceProcessor.SaveAsync(context, effectiveOutput, cancellationToken).ConfigureAwait(false);
                        break;
                }

                _logger.LogInformation("Obfuscation completed. Output written to {OutputPath}", effectiveOutput);

                if (settings.Packing.Enabled && target.TargetType != TargetType.Assembly)
                {
                    context.Warnings.Add(
                        "Packing skipped: it applies only to assemblies with an entry point, not source.");
                    _logger.LogWarning("Packing enabled but target {Path} is {Type}", inputPath, target.TargetType);
                }

                string? packedPath = null;
                if (settings.Packing.Enabled && target.TargetType == TargetType.Assembly)
                {
                    try
                    {
                        packedPath = PackingApplicator.Pack(
                            effectiveOutput, settings, inputPath, context.Warnings);
                        _logger.LogInformation("Packed output written to {Packed}", packedPath);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogError(ex, "Packing failed for {Output}", effectiveOutput);
                        return ObfuscationResult.Failed($"Packing failed: {ex.Message}", ex);
                    }
                }

                if (settings.Incremental.Enabled && target.TargetType == TargetType.Assembly &&
                    !IncrementalCache.TryWrite(inputPath, effectiveOutput, settings))
                {
                    _logger.LogWarning("Could not write incremental cache for {Output}", effectiveOutput);
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
                _logger.LogError(ex, "Failed to save obfuscated output");
                return ObfuscationResult.Failed($"Failed to save output: {ex.Message}", ex);
            }
        }
        finally
        {
            runScope?.Dispose();
            // The loaded module holds native resources. SaveAsync disposes and nulls it on the
            // success path; dispose here too so a pipeline/write failure cannot leak it.
            if (context.Module is IDisposable module)
            {
                module.Dispose();
                context.Module = null;
            }
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

        settings = settings.Clone();
        if (settings.Level != ObfuscationLevel.Custom)
            settings.ApplyLevel();

        try
        {
            settings.Validate();
        }
        catch (ValidationException ex)
        {
            _logger.LogError(ex, "Invalid obfuscation settings");
            return ObfuscationResult.Failed($"Invalid settings: {ex.Message}", ex);
        }

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
            RuntimeProfileGating.Apply(settings, context);
            foreach (var warning in context.Warnings)
                _logger.LogWarning("{Warning}", warning);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to load source files");
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
                _logger.LogError(ex, "Failed to save obfuscated source");
                return ObfuscationResult.Failed($"Failed to save output: {ex.Message}", ex);
            }
        }
        finally
        {
            runScope?.Dispose();
            if (context.Module is IDisposable module)
            {
                module.Dispose();
                context.Module = null;
            }
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

        _logger.LogInformation("Symbol map written to {OutputPath} with {Count} entries",
            outputPath, symbolMap.Count);
    }

    /// <inheritdoc/>
    public async Task<ObfuscationResult> MergeAndObfuscateAsync(
        IEnumerable<string> inputPaths,
        string outputPath,
        ObfySettings settings,
        CancellationToken cancellationToken = default)
    {
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
                _logger.LogError("Assembly merge failed: {Error}", mergeResult.ErrorMessage);
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
                cancellationToken).ConfigureAwait(false);

            // Add merge info to the result
            if (obfuscationResult.Success)
            {
                _logger.LogInformation(
                    "Successfully merged and obfuscated {Count} assemblies to {Output}",
                    mergeResult.MergedAssemblyCount,
                    outputPath);
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
                _logger.LogWarning(ex, "Failed to cleanup temp directory: {TempDir}", tempDir);
            }
        }
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
