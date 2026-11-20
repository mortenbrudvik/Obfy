using System.Text.RegularExpressions;
using Shouldly;

namespace Obfy.Tests;

/// <summary>Static assertions on Obfy.UI.csproj: the frontend build must be wired into build and publish.</summary>
public class HybridBuildTests
{
    private const string Guard = "Condition=\"'$(SkipFrontendBuild)' != 'true'\"";

    private static readonly string UiProjectPath = Path.Combine(FindRepoRoot(), "Src", "Obfy.UI", "Obfy.UI.csproj");

    [Fact]
    public void UiCsproj_HasFrontendTargets()
    {
        var csproj = File.ReadAllText(UiProjectPath);

        // Both targets that call npm carry the SkipFrontendBuild guard on their own opening tag.
        OpeningTag(csproj, "NpmInstall").ShouldContain(Guard);
        OpeningTag(csproj, "BuildFrontend").ShouldContain(Guard);
        // The targets that move the output into the build and the publish exist.
        TargetBlock(csproj, "CopyFrontend").ShouldContain("Assets\\");
        TargetBlock(csproj, "IncludeFrontendInPublish").ShouldContain("ResolvedFileToPublish");
    }

    [Fact]
    public void UiCsproj_PublishErrorsOnEmptyDist()
    {
        var csproj = File.ReadAllText(UiProjectPath);

        // The empty-dist Error is inside the publish target, not just somewhere in the project.
        TargetBlock(csproj, "IncludeFrontendInPublish")
            .ShouldMatch(@"<Error\s+Condition=""'@\(_FrontendPublishFiles\)' == ''""[^>]*/>");
    }

    private static string TargetBlock(string csproj, string name)
    {
        var match = Regex.Match(csproj, $@"<Target\b[^>]*\bName=""{name}""[^>]*>.*?</Target>", RegexOptions.Singleline);
        match.Success.ShouldBeTrue($"Expected a <Target Name=\"{name}\"> block in Obfy.UI.csproj");
        return match.Value;
    }

    private static string OpeningTag(string csproj, string name)
    {
        var match = Regex.Match(csproj, $@"<Target\b[^>]*\bName=""{name}""[^>]*>", RegexOptions.Singleline);
        match.Success.ShouldBeTrue($"Expected a <Target Name=\"{name}\"> element in Obfy.UI.csproj");
        return match.Value;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Obfy.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Obfy.sln not found above the test output directory.");
    }
}
