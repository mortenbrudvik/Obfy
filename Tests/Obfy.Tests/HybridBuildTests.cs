using Shouldly;

namespace Obfy.Tests;

/// <summary>Static assertions on Obfy.UI.csproj: the frontend build must be wired into build and publish.</summary>
public class HybridBuildTests
{
    private static readonly string UiProjectPath = Path.Combine(FindRepoRoot(), "Src", "Obfy.UI", "Obfy.UI.csproj");

    [Fact]
    public void UiCsproj_HasFrontendTargets()
    {
        var csproj = File.ReadAllText(UiProjectPath);

        csproj.ShouldContain("Name=\"NpmInstall\"");
        csproj.ShouldContain("Name=\"BuildFrontend\"");
        csproj.ShouldContain("Name=\"CopyFrontend\"");
        csproj.ShouldContain("Name=\"IncludeFrontendInPublish\"");
        csproj.ShouldContain("Condition=\"'$(SkipFrontendBuild)' != 'true'\"");
        // The frontend build must not run on a SkipFrontendBuild build: both targets that call npm are guarded.
        const string guard = "Condition=\"'$(SkipFrontendBuild)' != 'true'\"";
        csproj.Split(guard).Length.ShouldBeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public void UiCsproj_PublishErrorsOnEmptyDist()
    {
        var csproj = File.ReadAllText(UiProjectPath);

        csproj.ShouldMatch(@"<Error\s+Condition=""'@\(_FrontendPublishFiles\)' == ''""");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Obfy.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Obfy.sln not found above the test output directory.");
    }
}
