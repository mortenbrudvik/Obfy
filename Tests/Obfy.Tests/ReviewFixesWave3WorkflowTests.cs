using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Shouldly;

namespace Obfy.Tests;

/// <summary>
/// Contract tests for Wave 3A supply-chain, CI/CD, and workflow hardening (<c>B-61</c>, <c>B-29</c>, <c>B-13</c>).
/// </summary>
public class ReviewFixesWave3WorkflowTests
{
    private static readonly string _repoRoot = FindRepoRoot();

    [Fact]
    public void GlobalJson_ExistsAndPinsDotnet10Sdk()
    {
        var path = Path.Combine(_repoRoot, "global.json");
        File.Exists(path).ShouldBeTrue("global.json must exist at repository root");

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        document.RootElement.TryGetProperty("sdk", out var sdk).ShouldBeTrue();

        var version = sdk.GetProperty("version").GetString();
        version.ShouldNotBeNullOrWhiteSpace();
        version.ShouldStartWith("10.0.");

        sdk.GetProperty("rollForward").GetString().ShouldBe("latestFeature");
    }

    [Fact]
    public void NugetConfig_ExistsWithClearedSourcesAndPackageSourceMapping()
    {
        var path = Path.Combine(_repoRoot, "nuget.config");
        File.Exists(path).ShouldBeTrue("nuget.config must exist at repository root");

        var doc = XDocument.Load(path);
        var packageSources = doc.Root?.Element("packageSources");
        packageSources.ShouldNotBeNull();
        packageSources.Element("clear").ShouldNotBeNull("nuget.config must clear inherited package sources");

        var adds = packageSources.Elements("add").ToList();
        adds.ShouldHaveSingleItem();
        adds[0].Attribute("key")?.Value.ShouldBe("nuget.org");
        adds[0].Attribute("value")?.Value.ShouldBe("https://api.nuget.org/v3/index.json");

        var mapping = doc.Root?.Element("packageSourceMapping");
        mapping.ShouldNotBeNull("nuget.config must declare packageSourceMapping");
        var mappedSource = mapping.Elements("packageSource").Single();
        mappedSource.Attribute("key")?.Value.ShouldBe("nuget.org");
        mappedSource.Elements("package").Single().Attribute("pattern")?.Value.ShouldBe("*");
    }

    [Fact]
    public void DependabotConfig_MonitorsGitHubActionsNuGetAndNpm()
    {
        var path = Path.Combine(_repoRoot, ".github", "dependabot.yml");
        File.Exists(path).ShouldBeTrue(".github/dependabot.yml must exist");

        var yaml = File.ReadAllText(path);
        yaml.ShouldContain("version: 2");
        AssertDependabotEcosystem(yaml, "github-actions", "/");
        AssertDependabotEcosystem(yaml, "nuget", "/");
        AssertDependabotEcosystem(yaml, "npm", "/Src/WebApp");
    }

    [Fact]
    public void CiWorkflow_DefinesMacOsCliJobRunningConsoleTests()
    {
        var yaml = ReadWorkflow("ci.yml");
        var jobBlock = ExtractJobBlock(yaml, "macos-cli");

        jobBlock.ShouldContain("runs-on: macos-latest");
        jobBlock.ShouldContain("timeout-minutes: 30");
        jobBlock.ShouldContain("actions/checkout@");
        jobBlock.ShouldContain("actions/setup-dotnet@");
        jobBlock.ShouldContain("10.0.x");
        jobBlock.ShouldContain("Tests/Obfy.Console.Tests/Obfy.Console.Tests.csproj");
        jobBlock.ShouldNotContain("dotnet build");
    }

    [Fact]
    public void Workflows_PinTimeoutsOnCiAndPublishJobs()
    {
        var ci = ReadWorkflow("ci.yml");
        ExtractJobBlock(ci, "build").ShouldContain("timeout-minutes: 45");
        ExtractJobBlock(ci, "linux-cli").ShouldContain("timeout-minutes: 30");
        ExtractJobBlock(ci, "macos-cli").ShouldContain("timeout-minutes: 30");

        var publish = ReadWorkflow("publish.yml");
        ExtractJobBlock(publish, "publish").ShouldContain("timeout-minutes: 45");
    }

    [Fact]
    public void Workflows_DoNotUseDeprecatedNode20ActionShas()
    {
        string[] deprecatedShas =
        [
            "11d5960a326750d5838078e36cf38b85af677262", // actions/checkout v4
            "67a3573c9a986a3f9c594539f4ab511d57bb3ce9", // actions/setup-dotnet v4
            "49933ea5288caeca8642d1e84afbd3f7d6820020", // actions/setup-node v4
            "ea165f8d65b6e75b540449e92b4886f43607fa02", // actions/upload-artifact v4
            "773744901bac0e8cbb5a0dc842800d45e9b2b405"  // marocchino/sticky-pull-request-comment v2
        ];

        var workflows = Directory.GetFiles(Path.Combine(_repoRoot, ".github", "workflows"), "*.yml");
        workflows.ShouldNotBeEmpty();

        foreach (var workflow in workflows)
        {
            var content = File.ReadAllText(workflow);
            foreach (var sha in deprecatedShas)
            {
                content.ShouldNotContain(sha, Case.Insensitive, $"{Path.GetFileName(workflow)} still uses deprecated Node 20 SHA {sha}");
            }
        }
    }

    [Fact]
    public void CiWorkflow_CheckCoverageThreshold_OnlyRunsOnSuccess()
    {
        var ci = ReadWorkflow("ci.yml");
        var buildJob = ExtractJobBlock(ci, "build");
        var stepBlock = ExtractStepBlock(buildJob, "Check Coverage Threshold");

        stepBlock.ShouldContain("if: success()");
        stepBlock.ShouldNotContain("if: success() || failure()");
    }

    private static void AssertDependabotEcosystem(string yaml, string ecosystem, string expectedDirectory)
    {
        var pattern = $@"-[ \t]*package-ecosystem:[ \t]*[""']?{Regex.Escape(ecosystem)}[""']?[ \t]*\r?\n[ \t]*directory:[ \t]*[""']?{Regex.Escape(expectedDirectory)}[""']?[ \t]*\r?\n[ \t]*schedule:[ \t]*\r?\n[ \t]*interval:[ \t]*[""']?weekly[""']?";
        Regex.IsMatch(yaml, pattern).ShouldBeTrue(
            $"dependabot.yml must configure weekly updates for {ecosystem} in {expectedDirectory}");
    }

    private static string ExtractJobBlock(string workflowYaml, string jobName)
    {
        var lines = workflowYaml.Split(["\r\n", "\n"], StringSplitOptions.None);
        var startIndex = Array.FindIndex(lines, line => Regex.IsMatch(line, $@"^  {Regex.Escape(jobName)}:\s*$"));
        startIndex.ShouldBeGreaterThanOrEqualTo(0, $"Job '{jobName}' not found");

        var block = new List<string> { lines[startIndex] };
        for (var i = startIndex + 1; i < lines.Length; i++)
        {
            if (Regex.IsMatch(lines[i], @"^  [A-Za-z0-9_-]+:\s*$") || (lines[i].Length > 0 && !char.IsWhiteSpace(lines[i][0])))
                break;
            block.Add(lines[i]);
        }

        return string.Join(Environment.NewLine, block);
    }

    private static string ExtractStepBlock(string jobBlock, string stepName)
    {
        var lines = jobBlock.Split(["\r\n", "\n"], StringSplitOptions.None);
        var startIndex = Array.FindIndex(lines, line => Regex.IsMatch(line, $@"^\s*- name:\s*{Regex.Escape(stepName)}\s*$"));
        startIndex.ShouldBeGreaterThanOrEqualTo(0, $"Step '{stepName}' not found");

        var indentLength = lines[startIndex].TakeWhile(c => c == ' ').Count();
        var block = new List<string> { lines[startIndex] };
        for (var i = startIndex + 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (!string.IsNullOrWhiteSpace(line) &&
                line.TakeWhile(c => c == ' ').Count() == indentLength &&
                line.TrimStart().StartsWith("- ", StringComparison.Ordinal))
            {
                break;
            }

            block.Add(line);
        }

        return string.Join(Environment.NewLine, block);
    }

    private static string ReadWorkflow(string name) =>
        File.ReadAllText(Path.Combine(_repoRoot, ".github", "workflows", name));

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
