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
    private readonly ILogger<ObfuscationService> _logger;

    public ObfuscationService(
        IAssemblyProcessor assemblyProcessor,
        ISourceProcessor sourceProcessor,
        IObfuscationPipeline pipeline,
        ILogger<ObfuscationService> logger)
    {
        _assemblyProcessor = assemblyProcessor;
        _sourceProcessor = sourceProcessor;
        _pipeline = pipeline;
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

        if (!File.Exists(inputPath))
        {
            return ObfuscationResult.Failed($"Input file not found: {inputPath}");
        }

        var target = ObfuscationTarget.FromFile(inputPath, outputPath);

        // Apply level presets to settings
        settings.ApplyLevel();

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
                effectiveOutput,
                result.ElapsedTime);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save obfuscated output");
            return ObfuscationResult.Failed($"Failed to save output: {ex.Message}", ex);
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

    private static string GenerateOutputPath(string inputPath)
    {
        var directory = Path.GetDirectoryName(inputPath) ?? ".";
        var name = Path.GetFileNameWithoutExtension(inputPath);
        var extension = Path.GetExtension(inputPath);
        return Path.Combine(directory, $"{name}.obfuscated{extension}");
    }
}
