using System.IO;

namespace Obfy.UI.AutomationTests;

/// <summary>
/// Resolves ObfyUI.exe for FlaUI tests from the current test output layout
/// (Debug or Release) instead of a hardcoded Debug path.
/// </summary>
internal static class UiExecutableLocator
{
    public const string FileName = "ObfyUI.exe";

    public static string DetectConfiguration(string baseDirectory)
    {
        var parts = baseDirectory.Split(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);

        for (var i = parts.Length - 1; i >= 0; i--)
        {
            if (parts[i].Equals("Release", StringComparison.OrdinalIgnoreCase))
                return "Release";
            if (parts[i].Equals("Debug", StringComparison.OrdinalIgnoreCase))
                return "Debug";
        }

        return "Debug";
    }

    public static string FindSolutionDirectory(string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Obfy.sln")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new InvalidOperationException($"Could not locate Obfy.sln from {startDirectory}");
    }

    public static string Resolve(string solutionDirectory, string tfm, string configurationHint)
    {
        var preferred = configurationHint.Equals("Release", StringComparison.OrdinalIgnoreCase)
            ? new[] { "Release", "Debug" }
            : new[] { "Debug", "Release" };

        var attempted = new List<string>(preferred.Length);
        foreach (var configuration in preferred)
        {
            var path = Path.Combine(solutionDirectory, "Src", "Obfy.UI", "bin", configuration, tfm, FileName);
            attempted.Add(path);
            if (File.Exists(path))
                return path;
        }

        throw new FileNotFoundException(
            $"{FileName} not found. Build the UI project first.\nTried:\n{string.Join("\n", attempted)}");
    }

    public static string ResolveFromTestContext(string baseDirectory)
    {
        var tfm = new DirectoryInfo(baseDirectory).Name;
        var configuration = DetectConfiguration(baseDirectory);
        var solutionDirectory = FindSolutionDirectory(baseDirectory);
        return Resolve(solutionDirectory, tfm, configuration);
    }
}
