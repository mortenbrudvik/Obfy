using System.Collections.Generic;
using System.IO;

namespace Obfy.VisualStudio.Services;

/// <summary>
/// Resolves a project's output assembly path without EnvDTE.
/// Prefers MSBuild OutputPath+OutputFileName when the file exists, then common bin fallbacks
/// (dll and exe, including <c>-windows</c> TFMs).
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

        foreach (var path in EnumerateFallbackPaths(projectDir!, projectName!, outputFileName))
        {
            if (File.Exists(path))
                return path;
        }

        return null;
    }

    public static IEnumerable<string> EnumerateFallbackPaths(
        string projectDir,
        string projectName,
        string? outputFileName = null)
    {
        var names = new List<string>();
        if (!string.IsNullOrEmpty(outputFileName))
            names.Add(outputFileName!);
        names.Add(projectName + ".dll");
        names.Add(projectName + ".exe");

        foreach (var config in new[] { "Release", "Debug" })
        {
            foreach (var tfm in new[]
                     {
                         "net10.0-windows", "net10.0",
                         "net9.0-windows", "net9.0",
                         "net8.0-windows", "net8.0"
                     })
            {
                foreach (var name in names)
                    yield return Path.Combine(projectDir, "bin", config, tfm, name);
            }

            foreach (var name in names)
                yield return Path.Combine(projectDir, "bin", config, name);
        }
    }
}
