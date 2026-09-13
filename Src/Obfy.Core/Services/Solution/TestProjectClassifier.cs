namespace Obfy.Core.Services.Solution;

/// <summary>
/// Heuristics for recognizing test projects by name.
/// </summary>
public static class TestProjectClassifier
{
    /// <summary>
    /// Returns true when <paramref name="projectFileNameWithoutExtension"/> looks like a test project.
    /// Matching is ordinal-ignore-case: equals <c>Test</c>, ends with <c>Tests</c> / <c>.Test</c> /
    /// <c>.Testing</c>, or contains <c>.Tests.</c>.
    /// </summary>
    public static bool IsTestProjectName(string projectFileNameWithoutExtension)
    {
        ArgumentNullException.ThrowIfNull(projectFileNameWithoutExtension);

        const StringComparison comparison = StringComparison.OrdinalIgnoreCase;

        if (projectFileNameWithoutExtension.Equals("Test", comparison))
            return true;

        if (projectFileNameWithoutExtension.EndsWith("Tests", comparison))
            return true;

        if (projectFileNameWithoutExtension.EndsWith(".Test", comparison))
            return true;

        if (projectFileNameWithoutExtension.EndsWith(".Testing", comparison))
            return true;

        if (projectFileNameWithoutExtension.Contains(".Tests.", comparison))
            return true;

        return false;
    }
}
