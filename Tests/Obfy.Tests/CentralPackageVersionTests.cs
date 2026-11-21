using System.IO.Enumeration;
using System.Xml.Linq;
using Shouldly;

namespace Obfy.Tests;

public class CentralPackageVersionTests : IDisposable
{
    private readonly string _scratch = Path.Combine(Path.GetTempPath(), $"ObfyPackages_{Guid.NewGuid():N}");

    public void Dispose()
    {
        try { Directory.Delete(_scratch, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public void PackageReferences_UseTheCentralVersionList()
    {
        var root = RepoRoot();
        var props = XDocument.Load(Path.Combine(root, "Directory.Packages.props"));
        props.Descendants("ManagePackageVersionsCentrally").Single().Value.ShouldBe("true");

        var versions = props.Descendants("PackageVersion")
            .ToDictionary(
                element => element.Attribute("Include")!.Value,
                element => element.Attribute("Version")!.Value,
                StringComparer.OrdinalIgnoreCase);

        versions.Count.ShouldBeGreaterThan(0);
        versions.Values.ShouldAllBe(version => !string.IsNullOrWhiteSpace(version));

        var references = 0;
        foreach (var project in EnumerateOwnProjects(root))
        {
            foreach (var reference in XDocument.Load(project).Descendants("PackageReference"))
            {
                references++;
                reference.Attribute("Version").ShouldBeNull(project);
                var id = reference.Attribute("Include")?.Value;
                id.ShouldNotBeNullOrWhiteSpace(project);
                versions.ContainsKey(id!).ShouldBeTrue($"No central version for {id} ({project})");
            }
        }

        references.ShouldBeGreaterThan(0);
    }

    [Theory]
    [InlineData(".worktrees")]
    [InlineData("bin")]
    [InlineData("obj")]
    [InlineData("node_modules")]
    public void EnumerateOwnProjects_SkipsProjectsBelowAnExcludedFolder(string folder)
    {
        var root = Path.Combine(_scratch, "repo");
        var own = Touch(root, "Src", "App", "App.csproj");
        Touch(root, folder, "old", "Src", "Old", "Old.csproj");
        Touch(root, "Src", "App", folder, "Nested.csproj");

        EnumerateOwnProjects(root).ShouldBe([own]);
    }

    [Theory]
    [InlineData(".worktrees")]
    [InlineData("bin")]
    [InlineData("obj")]
    [InlineData("node_modules")]
    public void EnumerateOwnProjects_KeepsProjectsWhenTheRootIsInsideAnExcludedFolder(string folder)
    {
        // Tests run from inside a worktree, so the repository root is itself <path>\.worktrees\<name>.
        var root = Path.Combine(_scratch, folder, "name");
        var app = Touch(root, "Src", "App", "App.csproj");
        var tests = Touch(root, "Tests", "App.Tests", "App.Tests.csproj");
        Touch(root, folder, "x", "Src", "Old", "Old.csproj");

        EnumerateOwnProjects(root).Order().ShouldBe(new[] { app, tests }.Order());
    }

    private string Touch(string root, params string[] relativeParts)
    {
        var path = Path.Combine([root, .. relativeParts]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "<Project />");
        return path;
    }

    /// <summary>
    /// Folders the scan does not enter: build output, package folders, and sibling worktrees (CLAUDE.md puts every
    /// feature branch in a worktree under <c>.worktrees</c>, each with its own full copy of the project files).
    /// </summary>
    private static readonly HashSet<string> SkippedFolders =
        new([".worktrees", "bin", "obj", "node_modules"], StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The <c>*.csproj</c> files of the checkout at <paramref name="root"/> and nothing else. Folders are judged by
    /// name below the root only, never by the absolute path, so a checkout that itself lives in
    /// <c>...\.worktrees\name</c> (where the tests run from a worktree) still finds its own projects.
    /// </summary>
    internal static IEnumerable<string> EnumerateOwnProjects(string root) =>
        new FileSystemEnumerable<string>(
            root,
            (ref FileSystemEntry entry) => entry.ToFullPath(),
            new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 })
        {
            ShouldIncludePredicate = (ref FileSystemEntry entry) =>
                !entry.IsDirectory && entry.FileName.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase),
            ShouldRecursePredicate = (ref FileSystemEntry entry) => !SkippedFolders.Contains(entry.FileName.ToString())
        };

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
