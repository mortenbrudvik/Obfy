using System.Text.RegularExpressions;

namespace Obfy.ScenarioTests;

/// <summary>
/// A theory that builds and runs an SDK app for one target framework. It is skipped, with the reason shown in the
/// test output, when this machine cannot run that framework (its runtime is not installed) or, with
/// <paramref name="windowsOnly"/>, when the platform is not Windows. It is never a silent pass, and it never skips
/// the framework the tests themselves run on.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class SdkRuntimeTheoryAttribute : TheoryAttribute
{
    public SdkRuntimeTheoryAttribute(string targetFramework, bool windowsOnly = false) =>
        Skip = InstalledRuntimes.SkipReason(targetFramework, windowsOnly);
}

/// <summary>The same for a test without data. See <see cref="SdkRuntimeTheoryAttribute"/>.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class SdkRuntimeFactAttribute : FactAttribute
{
    public SdkRuntimeFactAttribute(string targetFramework, bool windowsOnly = false) =>
        Skip = InstalledRuntimes.SkipReason(targetFramework, windowsOnly);
}

internal static class InstalledRuntimes
{
    private static readonly Lazy<string?> RuntimeList = new(ReadRuntimeList);
    private static readonly Regex DotNetMoniker = new(@"^net(?:coreapp)?(\d+)\.(\d+)$", RegexOptions.CultureInvariant);

    public static string? SkipReason(string targetFramework, bool windowsOnly)
    {
        if (windowsOnly && !OperatingSystem.IsWindows())
            return "Windows-only: method encryption and anti-dump use kernel32.";

        if (!Has(targetFramework))
            return $"The runtime for {targetFramework} is not installed, so the fixture cannot run.";

        // An SDK build of an older .NET Framework target compiles against reference assemblies that come from a
        // targeting pack or from a NuGet package. Without either on this machine (an offline box that never
        // restored them) the build would fail, not the code under test, so that is a skip too.
        if (IsNetFramework(targetFramework) && !HasNetFrameworkReferenceAssemblies(targetFramework))
        {
            return $"The reference assemblies for {targetFramework} are not on this machine (no targeting pack, and no " +
                   $"Microsoft.NETFramework.ReferenceAssemblies.{targetFramework} package in the NuGet cache); " +
                   "restore them once by building any project that targets it.";
        }

        return null;
    }

    /// <summary>
    /// The mscorlib of a .NET Framework version's reference assemblies, from the targeting pack (the older ones ship
    /// IntelliSense files only) or the Microsoft.NETFramework.ReferenceAssemblies package the SDK restores.
    /// </summary>
    public static bool HasNetFrameworkReferenceAssemblies(string targetFramework)
    {
        var digits = targetFramework["net".Length..];
        var version = string.Join('.', digits.ToCharArray());

        var pack = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Reference Assemblies", "Microsoft", "Framework", ".NETFramework", "v" + version, "mscorlib.dll");
        if (File.Exists(pack))
            return true;

        var packages = Environment.GetEnvironmentVariable("NUGET_PACKAGES") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
        var package = Path.Combine(packages, "microsoft.netframework.referenceassemblies." + targetFramework);
        return Directory.Exists(package) &&
               Directory.GetDirectories(package).Any(d =>
                   File.Exists(Path.Combine(d, "build", ".NETFramework", "v" + version, "mscorlib.dll")));
    }

    /// <summary>
    /// Whether the target framework can run here. The framework this process runs on always can. .NET Framework apps
    /// run on the Windows .NET Framework 4.x runtime. For modern .NET, <c>dotnet --list-runtimes</c> has to list
    /// Microsoft.NETCore.App for the version; when it cannot be read at all the answer is yes, so that the test runs
    /// and fails on the broken <c>dotnet</c> instead of every case being skipped.
    /// </summary>
    public static bool Has(string targetFramework)
    {
        if (targetFramework == $"net{Environment.Version.Major}.0")
            return true;

        if (IsNetFramework(targetFramework))
        {
            return OperatingSystem.IsWindows() &&
                   File.Exists(Path.Combine(
                       Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                       "Microsoft.NET", "Framework64", "v4.0.30319", "clr.dll"));
        }

        var match = DotNetMoniker.Match(targetFramework);
        if (!match.Success)
            throw new ArgumentException($"Not a target framework moniker: {targetFramework}", nameof(targetFramework));

        var list = RuntimeList.Value;
        if (list is null)
            return true;

        var prefix = $"Microsoft.NETCore.App {match.Groups[1].Value}.{match.Groups[2].Value}.";
        return list
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(line => line.StartsWith(prefix, StringComparison.Ordinal));
    }

    /// <summary>net48, net461 and the like: an executable that the .NET Framework runs, not a dll for <c>dotnet</c>.</summary>
    public static bool IsNetFramework(string targetFramework) =>
        Regex.IsMatch(targetFramework, @"^net4\d+$", RegexOptions.CultureInvariant);

    private static string? ReadRuntimeList()
    {
        try
        {
            var result = ScenarioHarness.RunProcess("dotnet", "--list-runtimes", Directory.GetCurrentDirectory(), 20_000);
            if (result.ExitCode == 0)
                return result.StdOut;

            System.Console.Error.WriteLine($"dotnet --list-runtimes exited {result.ExitCode}: {result.StdOut}{result.StdErr}");
        }
        catch (Exception ex)
        {
            System.Console.Error.WriteLine($"dotnet --list-runtimes failed: {ex}");
        }

        return null;
    }
}
