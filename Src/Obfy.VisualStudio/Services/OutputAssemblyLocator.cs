using System.Collections.Generic;
using System.IO;

namespace Obfy.VisualStudio.Services;

/// <summary>
/// Resolves a project's output assembly path without EnvDTE.
/// Prefers MSBuild OutputPath+OutputFileName when the file exists, then
/// <c>bin/{Release,Debug}/{net10.0,net9.0,net8.0}/{project}.dll</c> (exe projects rely on MSBuild OutputFileName).
/// </summary>
public static class OutputAssemblyLocator
{
    public static string? ResolveExisting(
        string? projectDir,
        string? outputPath,
        string? outputFileName,
        string? projectName)
    {
        if (!string.IsNullOrEmpty(projectDir) &&
            !string.IsNullOrEmpty(outputPath) &&
            !string.IsNullOrEmpty(outputFileName))
        {
            var fullPath = Path.Combine(projectDir, outputPath, outputFileName);
            if (File.Exists(fullPath))
                return fullPath;
        }

        if (string.IsNullOrEmpty(projectDir) || string.IsNullOrEmpty(projectName))
            return null;

        foreach (var path in EnumerateFallbackPaths(projectDir!, projectName!))
        {
            if (File.Exists(path))
                return path;
        }

        return null;
    }

    public static IEnumerable<string> EnumerateFallbackPaths(string projectDir, string projectName)
    {
        foreach (var config in new[] { "Release", "Debug" })
        {
            foreach (var tfm in new[] { "net10.0", "net9.0", "net8.0" })
                yield return Path.Combine(projectDir, "bin", config, tfm, projectName + ".dll");
            yield return Path.Combine(projectDir, "bin", config, projectName + ".dll");
        }
    }
}
