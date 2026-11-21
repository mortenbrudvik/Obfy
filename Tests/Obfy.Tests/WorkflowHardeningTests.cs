using Shouldly;

namespace Obfy.Tests;

/// <summary>
/// Static pins for the hardening that needs no repository settings: tool versions installed by the
/// workflows are fixed, and a manual publish can only run from main or a tag.
/// </summary>
public class WorkflowHardeningTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    [Fact]
    public void PublishWorkflow_OnlyRunsForReleasesOrMainAndTagRefs()
    {
        var workflow = Read(".github", "workflows", "publish.yml");
        var jobStart = workflow.IndexOf("  publish:", StringComparison.Ordinal);
        jobStart.ShouldBeGreaterThanOrEqualTo(0);
        var header = workflow[jobStart..workflow.IndexOf("    steps:", jobStart, StringComparison.Ordinal)];

        // workflow_dispatch can be started from any branch; without a guard that branch's code publishes to NuGet.
        header.ShouldContain("if:");
        header.ShouldContain("github.event_name == 'release'");
        header.ShouldContain("github.ref == 'refs/heads/main'");
        header.ShouldContain("startsWith(github.ref, 'refs/tags/')");
    }

    [Fact]
    public void PackageWorkflow_PinsTheInnoSetupVersion()
    {
        var workflow = Read(".github", "workflows", "package.yml");

        workflow.ShouldContain("choco install innosetup --version ");
    }

    [Fact]
    public void CiWorkflow_PinsTheReportGeneratorVersion()
    {
        var workflow = Read(".github", "workflows", "ci.yml");

        workflow.ShouldContain("dotnet tool install -g dotnet-reportgenerator-globaltool --version ");
    }

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine([RepoRoot, .. parts]));

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Obfy.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException($"Could not locate Obfy.sln from {AppContext.BaseDirectory}");
    }
}
