using Shouldly;

namespace Obfy.ScenarioTests;

/// <summary>
/// When a target framework cannot be built or run here, the scenario cases for it are skipped with the reason shown
/// (never failed, never silently passed); the framework the tests run on is never skipped.
/// </summary>
public class SdkRuntimeSkipTests
{
    private const string FakeFramework = "net499"; // 4.9.9: no targeting pack and no package can exist for it

    [Fact]
    public void TheFrameworkTheTestsRunOn_IsNeverSkipped()
    {
        InstalledRuntimes.SkipReason($"net{Environment.Version.Major}.0", windowsOnly: false).ShouldBeNull();
    }

    [Fact]
    public void ANetFrameworkTarget_WithoutReferenceAssemblies_IsSkippedWithTheReason()
    {
        var empty = NewDirectory();
        using (new NuGetCache(empty))
        {
            InstalledRuntimes.HasNetFrameworkReferenceAssemblies(FakeFramework).ShouldBeFalse();

            var reason = InstalledRuntimes.SkipReason(FakeFramework, windowsOnly: false);

            reason.ShouldNotBeNull();
            if (OperatingSystem.IsWindows())
                reason.ShouldContain("reference assemblies");
        }
    }

    [Fact]
    public void ANetFrameworkTarget_WithItsReferenceAssembliesInTheNuGetCache_IsNotSkipped()
    {
        var cache = NewDirectory();
        var versionDirectory = Path.Combine(
            cache, "microsoft.netframework.referenceassemblies." + FakeFramework, "1.0.0", "build", ".NETFramework", "v4.9.9");
        Directory.CreateDirectory(versionDirectory);
        File.WriteAllBytes(Path.Combine(versionDirectory, "mscorlib.dll"), Array.Empty<byte>());

        using (new NuGetCache(cache))
        {
            InstalledRuntimes.HasNetFrameworkReferenceAssemblies(FakeFramework).ShouldBeTrue();
            if (OperatingSystem.IsWindows())
                InstalledRuntimes.SkipReason(FakeFramework, windowsOnly: false).ShouldBeNull();
        }
    }

    private static string NewDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "obfy-nuget-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>Points NUGET_PACKAGES at a directory for the duration of a test (tests do not run in parallel).</summary>
    private sealed class NuGetCache : IDisposable
    {
        private readonly string? _previous = Environment.GetEnvironmentVariable("NUGET_PACKAGES");

        public NuGetCache(string directory) => Environment.SetEnvironmentVariable("NUGET_PACKAGES", directory);

        public void Dispose() => Environment.SetEnvironmentVariable("NUGET_PACKAGES", _previous);
    }
}
