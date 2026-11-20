using System.Xml.Linq;
using Shouldly;

namespace Obfy.Tests;

public class CentralPackageVersionTests
{
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
        foreach (var project in Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories))
        {
            if (IsBuildOutput(project))
                continue;

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

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
        || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);

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
