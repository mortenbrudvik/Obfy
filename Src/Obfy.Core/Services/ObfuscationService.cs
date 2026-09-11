using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Obfy.Core.Models;
using Obfy.Core.Pipeline;

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

    public ObfuscationService(
        IAssemblyProcessor assemblyProcessor,
        ISourceProcessor sourceProcessor,
        IObfuscationPipeline pipeline,
        IAssemblyMerger assemblyMerger,
        ILogger<ObfuscationService> logger)
    {
        _assemblyProcessor = assemblyProcessor;
        _sourceProcessor = sourceProcessor;
        _pipeline = pipeline;
        _assemblyMerger = assemblyMerger;
        _logger = logger;
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

        PipelineContext context;

        try
        {
            context = target.TargetType switch
            {
                TargetType.Assembly => await _assemblyProcessor.LoadAsync(inputPath, settings, cancellationToken),
                TargetType.SourceCode => await _sourceProcessor.LoadAsync(inputPath, settings, cancellationToken),
                _ => throw new ArgumentException($"Unsupported target type: {target.TargetType}")
            };

            context.InputPath = inputPath;
            context.OutputPath = target.EffectiveOutputPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load {InputPath}", inputPath);
            return ObfuscationResult.Failed($"Failed to load input: {ex.Message}", ex);
        }

        try
        {
            // Execute the pipeline
            var result = await _pipeline.ExecuteAsync(context, cancellationToken);

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
                        await _assemblyProcessor.SaveAsync(context, effectiveOutput, cancellationToken);
                        break;
                    case TargetType.SourceCode:
                        await _sourceProcessor.SaveAsync(context, effectiveOutput, cancellationToken);
                        break;
                }

                _logger.LogInformation("Obfuscation completed. Output written to {OutputPath}", effectiveOutput);

                return ObfuscationResult.Successful(
                    context.Statistics,
                    inputPath: inputPath,
                    outputPath: effectiveOutput,
                    elapsedTime: result.ElapsedTime,
                    processingTimes: context.ProcessingTimes.ToList(),
                    skippedItems: context.SkippedItems.ToList(),
                    symbolMap: new Dictionary<string, string>(context.SymbolMap),
                    warnings: context.Warnings.ToList());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save obfuscated output");
                return ObfuscationResult.Failed($"Failed to save output: {ex.Message}", ex);
            }
        }
        finally
        {
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
        var results = new List<ObfuscationResult>();

        foreach (var inputPath in inputPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var outputPath = Path.Combine(outputDirectory, Path.GetFileName(inputPath));
            var result = await ObfuscateAsync(inputPath, outputPath, settings, cancellationToken);
            results.Add(result);
        }

        return results;
    }

    /// <inheritdoc/>
    public async Task WriteSymbolMapAsync(Dictionary<string, string> symbolMap, string outputPath)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        var json = JsonSerializer.Serialize(symbolMap, options);
        await File.WriteAllTextAsync(outputPath, json);

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
                cancellationToken);

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
                cancellationToken);

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

    private static string GenerateOutputPath(string inputPath)
    {
        var directory = Path.GetDirectoryName(inputPath) ?? ".";
        var name = Path.GetFileNameWithoutExtension(inputPath);
        var extension = Path.GetExtension(inputPath);
        return Path.Combine(directory, $"{name}.obfuscated{extension}");
    }
}
