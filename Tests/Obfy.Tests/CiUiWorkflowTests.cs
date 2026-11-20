using Shouldly;

namespace Obfy.Tests;

public class CiUiWorkflowTests
{
    [Fact]
    public void WindowsCi_RunsCategoryUi()
    {
        var yaml = File.ReadAllText(Path.Combine(RepoRoot(), ".github", "workflows", "ci.yml"));
        yaml.ShouldContain("Tests/Obfy.UI.AutomationTests/Obfy.UI.AutomationTests.csproj");
        yaml.ShouldContain("--filter \"Category=UI\"");
    }

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
