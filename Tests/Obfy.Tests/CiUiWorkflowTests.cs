using Shouldly;

namespace Obfy.Tests;

public class CiUiWorkflowTests
{
    [Fact]
    public void WindowsCi_RunsCategoryUi()
    {
        var yaml = ReadCi();
        yaml.ShouldContain("Tests/Obfy.UI.AutomationTests/Obfy.UI.AutomationTests.csproj");
        yaml.ShouldContain("--filter \"Category=UI\"");
    }

    [Fact]
    public void WindowsCi_SetsUpNodeAndRunsTheWebTests()
    {
        var yaml = ReadCi();
        yaml.ShouldContain("actions/setup-node@");
        yaml.ShouldContain("npm ci");
        yaml.ShouldContain("npm run lint");
        yaml.ShouldContain("npm run test:run");
        yaml.ShouldContain("working-directory: Src/WebApp");
    }

    [Fact]
    public void WindowsCi_SetsUpNodeBeforeTheDotnetBuild()
    {
        // Obfy.UI.csproj runs npm as part of "dotnet build"; without Node on PATH first the build fails.
        var yaml = ReadCi();
        var node = yaml.IndexOf("actions/setup-node@", StringComparison.Ordinal);
        var build = yaml.IndexOf("run: dotnet build ", StringComparison.Ordinal);
        node.ShouldBeGreaterThan(-1);
        build.ShouldBeGreaterThan(-1);
        node.ShouldBeLessThan(build);
    }

    [Fact]
    public void WindowsCi_RunsTheUiTestsAfterTheBuild()
    {
        var yaml = ReadCi();
        var build = yaml.IndexOf("run: dotnet build ", StringComparison.Ordinal);
        var ui = yaml.IndexOf("--filter \"Category=UI\"", StringComparison.Ordinal);
        build.ShouldBeLessThan(ui);
    }

    private static string ReadCi() =>
        File.ReadAllText(Path.Combine(RepoRoot(), ".github", "workflows", "ci.yml"));

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Obfy.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not find the repository root.");
    }
}
