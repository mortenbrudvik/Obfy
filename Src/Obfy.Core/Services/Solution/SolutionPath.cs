namespace Obfy.Core.Services.Solution;

/// <summary>
/// Resolves paths written in Visual Studio <c>.sln</c> files.
/// Those files always use backslash separators, including on Unix.
/// </summary>
internal static class SolutionPath
{
    public static string Normalize(string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);
        return relativePath.Replace('\\', Path.DirectorySeparatorChar);
    }

    public static string Resolve(string baseDirectory, string relativePath)
    {
        ArgumentNullException.ThrowIfNull(baseDirectory);
        return Path.GetFullPath(Normalize(relativePath), baseDirectory);
    }
}
