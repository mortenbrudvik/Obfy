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

            WarnAboutInapplicableTechniques(context, applicableObfuscators);

            foreach (var obfuscator in applicableObfuscators)
            {
                cancellationToken.ThrowIfCancellationRequested();

                _logger.LogDebug("Executing obfuscator: {Name}", obfuscator.Name);
                var obfuscatorStopwatch = Stopwatch.StartNew();

                try
                {
                    var result = await obfuscator.ObfuscateAsync(context, cancellationToken).ConfigureAwait(false);

                    obfuscatorStopwatch.Stop();

                    if (!result.Success)
                    {
                        // The message can name symbols or paths from the user's input: it goes to the result, not the log.
                        _logger.LogError("Obfuscator {Name} failed", obfuscator.Name);
                        return ObfuscationResult.Failed(
                            $"Obfuscator '{obfuscator.Name}' failed: {result.ErrorMessage}",
                            result.Exception);
                    }

                    // Merge statistics
                    context.Statistics.Merge(result.Statistics);

                    // Track processing time for reporting
                    context.ProcessingTimes.Add(new ProcessingTimeEntry
                    {
                        ObfuscatorName = obfuscator.Name,
                        Duration = obfuscatorStopwatch.Elapsed,
                        TransformationsApplied = result.Statistics.TotalTransformations
                    });

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
                    _logger.LogError(
                        "Obfuscator {Name} threw an exception ({ExceptionType})", obfuscator.Name, ex.GetType().Name);
                    return ObfuscationResult.Failed($"Obfuscator '{obfuscator.Name}' threw an exception: {ex.Message}", ex);
                }
            }

            stopwatch.Stop();

            _logger.LogInformation("Pipeline completed in {ElapsedMs}ms with {TotalTransformations} total transformations",
                stopwatch.ElapsedMilliseconds,
                context.Statistics.TotalTransformations);

            return ObfuscationResult.Successful(
                context.Statistics,
                outputPath: context.OutputPath,
                elapsedTime: stopwatch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Pipeline was cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError("Pipeline failed with unexpected error ({ExceptionType})", ex.GetType().Name);
            return ObfuscationResult.Failed($"Pipeline failed: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// A technique the user enabled that cannot run on this kind of input (an assembly-only protection on source
    /// code, or the reverse) would otherwise vanish silently. Some techniques have one implementation per target
    /// at the same phase (string encryption, control flow, renaming). Those are covered when the counterpart
    /// runs, so only a technique with no enabled counterpart is reported.
    /// </summary>
    private void WarnAboutInapplicableTechniques(PipelineContext context, List<IObfuscator> applicable)
    {
        var targetLabel = context.TargetType == TargetType.SourceCode ? "source code" : "assembly";
        var skipped = new List<string>();

        foreach (var obfuscator in _obfuscators)
        {
            if (obfuscator.SupportsTargetType(context.TargetType) || !obfuscator.IsEnabled(context.Settings))
                continue;

            if (applicable.Any(other => other.Priority == obfuscator.Priority))
                continue;

            skipped.Add(obfuscator.Name);
        }

        // One line however many there are: a preset enables several assembly-only protections, so one warning per
        // technique would bury the run's real warnings under routine ones on every source run.
        if (skipped.Count == 1)
            context.Warnings.Add($"{skipped[0]} is enabled but does not apply to {targetLabel} input, so it was skipped.");
        else if (skipped.Count > 1)
            context.Warnings.Add(
                $"{skipped.Count} enabled protections do not apply to {targetLabel} input and were skipped: " +
                $"{string.Join(", ", skipped)}.");
    }
}
