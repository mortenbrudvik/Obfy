using System.IO.Enumeration;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Shouldly;

namespace Obfy.Tests;

/// <summary>
/// Contract tests for NuGet lock-file and locked-mode restore hardening (<c>B-61</c>).
/// </summary>
public class NuGetLockFileTests
{
    private static readonly string _repoRoot = FindRepoRoot();
    private static readonly HashSet<string> _skippedFolders =
        new([".worktrees", "bin", "obj", "node_modules"], StringComparer.OrdinalIgnoreCase);

    private static readonly Regex _slnProjectRegex = new(
        @"^Project\(""[^""]+""\)\s*=\s*""[^""]+"",\s*""([^""]+\.csproj)"",",
        RegexOptions.Multiline | RegexOptions.Compiled,
        TimeSpan.FromSeconds(5));

    [Fact]
    public void DirectoryBuildProps_ConfiguresNuGetLockFilesAndLockedMode()
    {
        var propsPath = Path.Combine(_repoRoot, "Directory.Build.props");
        var doc = XDocument.Load(propsPath);
        var root = doc.Root;
        root.ShouldNotBeNull();

        var lockGroup = root.Elements("PropertyGroup")
            .Single(g => g.Element("RestorePackagesWithLockFile") is not null);

        var groupCondition = lockGroup.Attribute("Condition")?.Value;
        groupCondition.ShouldNotBeNullOrWhiteSpace();
        groupCondition.ShouldContain("ObfySrcDirectory");
        groupCondition.ShouldContain("ObfyTestsDirectory");
        groupCondition.ShouldContain("!$(MSBuildProjectFullPath.StartsWith('$(ObfyFixturesDirectory)'");

        lockGroup.Element("RestorePackagesWithLockFile")?.Value.ShouldBe("true");

        var lockedMode = lockGroup.Element("RestoreLockedMode");
        lockedMode.ShouldNotBeNull();
        lockedMode.Value.ShouldBe("true");
        var lockedCondition = lockedMode.Attribute("Condition")?.Value;
        lockedCondition.ShouldNotBeNullOrWhiteSpace();
        lockedCondition.ShouldContain("$(ContinuousIntegrationBuild)");
        lockedCondition.ShouldContain("$(CI)");

        var lockFilePath = lockGroup.Element("NuGetLockFilePath");
        lockFilePath.ShouldNotBeNull();
        lockFilePath.Value.ShouldBe("packages.win-x64.lock.json");
        var ridCondition = lockFilePath.Attribute("Condition")?.Value;
        ridCondition.ShouldNotBeNullOrWhiteSpace();
        ridCondition.ShouldContain("$(RuntimeIdentifier)");
        ridCondition.ShouldContain("$(RuntimeIdentifiers)");
        ridCondition.ShouldContain("win-x64");
    }

    [Fact]
    public void SolutionProjects_HavePackagesLockFiles_WhileFixturesAndExamplesDoNot()
    {
        var slnProjects = ParseSolutionProjects(Path.Combine(_repoRoot, "Obfy.sln"));
        slnProjects.ShouldNotBeEmpty();

        foreach (var projectPath in slnProjects)
        {
            File.Exists(projectPath).ShouldBeTrue($"Missing solution project {projectPath}");
            var projectDir = Path.GetDirectoryName(projectPath)!;
            AssertValidLockFile(Path.Combine(projectDir, "packages.lock.json"));
        }

        // Self-contained win-x64 publish roots (Obfy.Console and Obfy.UI) and their transitive ProjectReferences
        // require packages.win-x64.lock.json as configured by NuGetLockFilePath in Directory.Build.props.
        var winX64Closure = CollectProjectClosure(
            Path.Combine(_repoRoot, "Src", "Obfy.Console", "Obfy.Console.csproj"),
            Path.Combine(_repoRoot, "Src", "Obfy.UI", "Obfy.UI.csproj"));
        winX64Closure.ShouldNotBeEmpty();

        foreach (var projectPath in winX64Closure)
        {
            var projectDir = Path.GetDirectoryName(projectPath)!;
            AssertValidLockFile(Path.Combine(projectDir, "packages.win-x64.lock.json"));
        }

        AssertNoLockFilesUnder(Path.Combine(_repoRoot, "Tests", "Obfy.ScenarioTests", "Fixtures"));
        AssertNoLockFilesUnder(Path.Combine(_repoRoot, "examples"));
    }

    [Theory]
    [InlineData("ci.yml", "build", "dotnet restore --locked-mode")]
    [InlineData("ci.yml", "linux-cli", "-p:RestoreLockedMode=true")]
    [InlineData("ci.yml", "macos-cli", "-p:RestoreLockedMode=true")]
    [InlineData("publish.yml", "publish", "dotnet restore Obfy.sln --locked-mode")]
    [InlineData("platform.yml", "native-aot", "dotnet restore --locked-mode")]
    [InlineData("platform.yml", "platform", "dotnet restore --locked-mode")]
    public void Workflows_EnforceLockedModeRestore(string workflowFile, string jobName, string expectedRestoreToken)
    {
        var yaml = File.ReadAllText(Path.Combine(_repoRoot, ".github", "workflows", workflowFile));
        var jobBlock = ExtractJobBlock(yaml, jobName);

        jobBlock.ShouldContain(expectedRestoreToken);

        foreach (var line in jobBlock.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith('#'))
                continue;

            if (trimmed.Contains("dotnet restore", StringComparison.Ordinal))
            {
                trimmed.ShouldContain("--locked-mode", Case.Sensitive,
                    $"{workflowFile} job '{jobName}' runs dotnet restore without --locked-mode: {trimmed}");
            }
        }
    }

    private static List<string> ParseSolutionProjects(string slnPath)
    {
        var content = File.ReadAllText(slnPath);
        var projects = new List<string>();
        foreach (Match match in _slnProjectRegex.Matches(content))
        {
            var relative = NormalizePathSeparators(match.Groups[1].Value);
            projects.Add(Path.GetFullPath(Path.Combine(_repoRoot, relative)));
        }

        return projects;
    }

    private static HashSet<string> CollectProjectClosure(params string[] rootProjects)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>();

        foreach (var root in rootProjects)
        {
            var full = Path.GetFullPath(root);
            if (visited.Add(full))
                queue.Enqueue(full);
        }

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            var currentDir = Path.GetDirectoryName(current)!;
            var doc = XDocument.Load(current);

            foreach (var reference in doc.Descendants("ProjectReference"))
            {
                var include = reference.Attribute("Include")?.Value;
                if (string.IsNullOrWhiteSpace(include))
                    continue;

                var target = Path.GetFullPath(Path.Combine(currentDir, NormalizePathSeparators(include)));
                if (visited.Add(target))
                    queue.Enqueue(target);
            }
        }

        return visited;
    }

    private static void AssertValidLockFile(string lockFilePath)
    {
        File.Exists(lockFilePath).ShouldBeTrue($"Expected NuGet lock file at {lockFilePath}");

        using var document = JsonDocument.Parse(File.ReadAllText(lockFilePath));
        document.RootElement.TryGetProperty("version", out var version).ShouldBeTrue(
            $"Lock file missing 'version': {lockFilePath}");
        version.GetInt32().ShouldBeGreaterThanOrEqualTo(1, lockFilePath);

        document.RootElement.TryGetProperty("dependencies", out var deps).ShouldBeTrue(
            $"Lock file missing 'dependencies': {lockFilePath}");
        deps.ValueKind.ShouldBe(JsonValueKind.Object, lockFilePath);
    }

    private static void AssertNoLockFilesUnder(string rootDir)
    {
        Directory.Exists(rootDir).ShouldBeTrue($"Expected directory {rootDir}");

        var projects = new FileSystemEnumerable<string>(
            rootDir,
            (ref FileSystemEntry entry) => entry.ToFullPath(),
            new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 })
        {
            ShouldIncludePredicate = (ref FileSystemEntry entry) =>
                !entry.IsDirectory && entry.FileName.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase),
            ShouldRecursePredicate = (ref FileSystemEntry entry) => !_skippedFolders.Contains(entry.FileName.ToString())
        }.ToList();

        projects.ShouldNotBeEmpty($"Expected .csproj files under {rootDir}");

        foreach (var project in projects)
        {
            var dir = Path.GetDirectoryName(project)!;
            Directory.GetFiles(dir, "packages*.lock.json").ShouldBeEmpty(
                $"Excluded project {project} must not contain NuGet lock files");
        }
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

    private static string NormalizePathSeparators(string path) =>
        path.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Obfy.sln")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new InvalidOperationException($"Could not locate Obfy.sln from {AppContext.BaseDirectory}");
    }
}
