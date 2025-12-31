using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Obfy.Core.Models;
using Obfy.Core.Obfuscators;

namespace Obfy.Core.Pipeline;

/// <summary>
/// Default implementation of the obfuscation pipeline.
/// Executes obfuscators in priority order.
/// </summary>
public class ObfuscationPipeline : IObfuscationPipeline
{
    private readonly ILogger<ObfuscationPipeline> _logger;
    private readonly List<IObfuscator> _obfuscators;

    /// <inheritdoc/>
    public IReadOnlyList<IObfuscator> Obfuscators => _obfuscators.AsReadOnly();

    /// <summary>
    /// Creates a new pipeline with the specified obfuscators.
    /// </summary>
    /// <param name="obfuscators">The obfuscators to include in the pipeline.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    public ObfuscationPipeline(IEnumerable<IObfuscator> obfuscators, ILogger<ObfuscationPipeline> logger)
    {
        _logger = logger;
        _obfuscators = obfuscators.OrderBy(o => o.Priority).ToList();

        _logger.LogDebug("Pipeline initialized with {Count} obfuscators: {Names}",
            _obfuscators.Count,
            string.Join(", ", _obfuscators.Select(o => $"{o.Name}({o.Priority})")));
    }

    /// <inheritdoc/>
    public async Task<ObfuscationResult> ExecuteAsync(PipelineContext context, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation("Starting obfuscation pipeline for {TargetType}", context.TargetType);

        try
        {
            var applicableObfuscators = _obfuscators
                .Where(o => o.SupportsTargetType(context.TargetType))
                .Where(o => o.IsEnabled(context.Settings))
                .ToList();

            _logger.LogDebug("Executing {Count} applicable obfuscators", applicableObfuscators.Count);

            foreach (var obfuscator in applicableObfuscators)
            {
                cancellationToken.ThrowIfCancellationRequested();

                _logger.LogDebug("Executing obfuscator: {Name}", obfuscator.Name);
                var obfuscatorStopwatch = Stopwatch.StartNew();

                try
                {
                    var result = await obfuscator.ObfuscateAsync(context, cancellationToken);

                    obfuscatorStopwatch.Stop();

                    if (!result.Success)
                    {
                        _logger.LogError("Obfuscator {Name} failed: {Error}", obfuscator.Name, result.ErrorMessage);
                        return ObfuscationResult.Failed(
                            $"Obfuscator '{obfuscator.Name}' failed: {result.ErrorMessage}",
                            result.Exception);
                    }

                    // Merge statistics
                    context.Statistics.Merge(result.Statistics);

                    _logger.LogDebug("Obfuscator {Name} completed in {ElapsedMs}ms with {Transformations} transformations",
                        obfuscator.Name,
                        obfuscatorStopwatch.ElapsedMilliseconds,
                        result.Statistics.TotalTransformations);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Obfuscator {Name} threw an exception", obfuscator.Name);
                    return ObfuscationResult.Failed($"Obfuscator '{obfuscator.Name}' threw an exception: {ex.Message}", ex);
                }
            }

            stopwatch.Stop();

            _logger.LogInformation("Pipeline completed in {ElapsedMs}ms with {TotalTransformations} total transformations",
                stopwatch.ElapsedMilliseconds,
                context.Statistics.TotalTransformations);

            return ObfuscationResult.Successful(
                context.Statistics,
                context.OutputPath,
                stopwatch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Pipeline was cancelled");
            return ObfuscationResult.Failed("Operation was cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Pipeline failed with unexpected error");
            return ObfuscationResult.Failed($"Pipeline failed: {ex.Message}", ex);
        }
    }
}
