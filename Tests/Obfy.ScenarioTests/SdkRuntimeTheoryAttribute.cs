using System.Text.RegularExpressions;

namespace Obfy.ScenarioTests;

/// <summary>
/// A theory that builds and runs an SDK app for one target framework. It is skipped, with the reason shown in the
/// test output, when this machine cannot run that framework (its shared runtime is not installed) or, with
/// <paramref name="windowsOnly"/>, when the platform is not Windows. It is never a silent pass.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class SdkRuntimeTheoryAttribute : TheoryAttribute
{
    public SdkRuntimeTheoryAttribute(string targetFramework, bool windowsOnly = false)
    {
        if (windowsOnly && !OperatingSystem.IsWindows())
        {
            Skip = "Windows-only: method encryption and anti-dump use kernel32.";
            return;
        }

        if (!InstalledRuntimes.Has(targetFramework))
            Skip = $"The .NET runtime for {targetFramework} is not installed, so the fixture cannot run.";
    }
}

internal static class InstalledRuntimes
{
    private static readonly Lazy<string> RuntimeList = new(ReadRuntimeList);
    private static readonly Regex TargetFramework = new(@"^net(\d+)\.(\d+)$", RegexOptions.CultureInvariant);

    /// <summary>True when <c>dotnet --list-runtimes</c> lists Microsoft.NETCore.App for the target framework's version.</summary>
    public static bool Has(string targetFramework)
    {
        var match = TargetFramework.Match(targetFramework);
        if (!match.Success)
            throw new ArgumentException($"Not a netX.Y target framework: {targetFramework}", nameof(targetFramework));

        var prefix = $"Microsoft.NETCore.App {match.Groups[1].Value}.{match.Groups[2].Value}.";
        return RuntimeList.Value
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(line => line.StartsWith(prefix, StringComparison.Ordinal));
    }

    private static string ReadRuntimeList()
    {
        try
        {
            var result = ScenarioHarness.RunProcess("dotnet", "--list-runtimes", Directory.GetCurrentDirectory(), 20_000);
            return result.ExitCode == 0 ? result.StdOut : string.Empty;
        }
        catch (Exception ex)
        {
            System.Console.Error.WriteLine($"dotnet --list-runtimes failed: {ex}");
            return string.Empty;
        }
    }
}
