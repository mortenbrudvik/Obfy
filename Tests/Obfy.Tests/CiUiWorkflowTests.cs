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

    [Fact]
    public void EveryWorkflowThatBuildsTheUi_SetsUpNodeFirst()
    {
        // Building Obfy.sln (or packaging, which publishes Obfy.UI) runs npm through Obfy.UI.csproj's frontend targets,
        // so a job that does it without -p:SkipFrontendBuild=true needs Node on PATH before that step.
        var workflows = Directory.GetFiles(Path.Combine(RepoRoot(), ".github", "workflows"), "*.yml");
        workflows.ShouldNotBeEmpty();

        var checkedJobs = 0;
        foreach (var workflow in workflows)
        {
            foreach (var (job, lines) in JobsOf(File.ReadAllLines(workflow)))
            {
                var steps = lines.Where(static l => !l.TrimStart().StartsWith('#')).ToList();
                var build = steps.FindIndex(static l =>
                    l.Contains("dotnet build", StringComparison.Ordinal)
                    || l.Contains("build-msix.ps1", StringComparison.Ordinal)
                    || l.Contains("build-installer.ps1", StringComparison.Ordinal));
                if (build < 0 || steps.Any(static l => l.Contains("SkipFrontendBuild", StringComparison.Ordinal)))
                    continue;

                checkedJobs++;
                var node = steps.FindIndex(static l => l.Contains("actions/setup-node@", StringComparison.Ordinal));
                var where = $"{Path.GetFileName(workflow)} job '{job}'";
                node.ShouldBeGreaterThan(-1, $"{where} builds the UI but never sets up Node");
                node.ShouldBeLessThan(build, $"{where} sets up Node after the step that builds the UI");
            }
        }

        checkedJobs.ShouldBeGreaterThanOrEqualTo(5, "ci, publish, package and both platform jobs build the UI");
    }

    /// <summary>The lines of each job under <c>jobs:</c> (a two-space-indented key starts one).</summary>
    private static IEnumerable<(string Name, List<string> Lines)> JobsOf(string[] lines)
    {
        string? name = null;
        List<string> current = [];
        var inJobs = false;
        foreach (var line in lines)
        {
            if (line.StartsWith("jobs:", StringComparison.Ordinal))
            {
                inJobs = true;
                continue;
            }

            if (!inJobs)
                continue;

            var header = System.Text.RegularExpressions.Regex.Match(line, @"^  ([A-Za-z0-9_-]+):\s*$");
            if (header.Success)
            {
                if (name is not null)
                    yield return (name, current);
                name = header.Groups[1].Value;
                current = [];
            }
            else if (name is not null)
            {
                current.Add(line);
            }
        }

        if (name is not null)
            yield return (name, current);
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
