using Microsoft.CodeAnalysis;

namespace Obfy.Core.Obfuscators.Source;

/// <summary>
/// Compares a source obfuscator's output with its input. The compilation Obfy works on only has the
/// framework's references and nothing recompiles the output, so a transformation that breaks the code
/// would otherwise be written out without a word. Errors the input already had (typically unresolved
/// package references) are not counted.
/// </summary>
internal static class SourceOutputCheck
{
    /// <summary>
    /// Adds a warning when <paramref name="after"/> has compile errors that <paramref name="before"/> did
    /// not. Only diagnostic ids and counts go into the message, never names or paths.
    /// </summary>
    public static void WarnOnNewErrors(
        string technique,
        Compilation before,
        Compilation after,
        ICollection<string> warnings,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // A clean output needs no baseline, which keeps the common case to one compile pass.
            var current = ErrorCounts(after, cancellationToken);
            if (current.Count == 0)
                return;

            var baseline = ErrorCounts(before, cancellationToken);
            var introduced = current
                .Select(pair => (Id: pair.Key, Extra: pair.Value - baseline.GetValueOrDefault(pair.Key)))
                .Where(item => item.Extra > 0)
                .OrderBy(item => item.Id, StringComparer.Ordinal)
                .ToList();
            if (introduced.Count == 0)
                return;

            var ids = string.Join(", ", introduced.Take(5).Select(item => item.Id));
            warnings.Add(
                $"{technique} produced {introduced.Sum(item => item.Extra)} compile error(s) that the input did not have " +
                $"({ids}). Build the output and review it before using it.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The check is advisory; it must never fail a run.
        }
    }

    private static Dictionary<string, int> ErrorCounts(Compilation compilation, CancellationToken cancellationToken) =>
        compilation.GetDiagnostics(cancellationToken)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .GroupBy(d => d.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
}
