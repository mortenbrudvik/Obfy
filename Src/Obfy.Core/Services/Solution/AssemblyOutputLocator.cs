namespace Obfy.Core.Services.Solution;

/// <summary>
/// Locates built assembly outputs under conventional <c>bin/Release</c> and <c>bin/Debug</c> layouts.
/// </summary>
public static class AssemblyOutputLocator
{
    /// <summary>
    /// Finds output assemblies for <paramref name="assemblyName"/> under <paramref name="projectDirectory"/>.
    /// Prefers Release over Debug per TFM; also checks old-style non-TFM folders. Does not return duplicates.
    /// </summary>
    public static IReadOnlyList<string> FindAll(
        string projectDirectory,
        string assemblyName,
        IReadOnlyList<string> targetFrameworks)
    {
        ArgumentNullException.ThrowIfNull(projectDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyName);
        ArgumentNullException.ThrowIfNull(targetFrameworks);

        var results = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var tfm in targetFrameworks)
        {
            if (string.IsNullOrWhiteSpace(tfm))
                continue;

            TryAddFirstExisting(
                results,
                seen,
                CandidatePaths(projectDirectory, assemblyName, tfm.Trim()));
        }

        // Old-style layout without a TFM segment (also when frameworks list is empty).
        TryAddFirstExisting(
            results,
            seen,
            CandidatePaths(projectDirectory, assemblyName, tfm: null));

        return results;
    }

    private static IEnumerable<string> CandidatePaths(string projectDirectory, string assemblyName, string? tfm)
    {
        foreach (var configuration in new[] { "Release", "Debug" })
        {
            var directory = tfm is null
                ? Path.Combine(projectDirectory, "bin", configuration)
                : Path.Combine(projectDirectory, "bin", configuration, tfm);

            yield return Path.Combine(directory, assemblyName + ".dll");
            yield return Path.Combine(directory, assemblyName + ".exe");
        }
    }

    private static void TryAddFirstExisting(List<string> results, HashSet<string> seen, IEnumerable<string> candidates)
    {
        foreach (var candidate in candidates)
        {
            if (!File.Exists(candidate))
                continue;

            if (!seen.Add(candidate))
                continue;

            results.Add(candidate);
            return;
        }
    }
}
