using System.Xml.Linq;
using Shouldly;

namespace Obfy.VisualStudio.Tests;

/// <summary>
/// The VSIX manifest names a license and icon files by relative path. They are only in the package when the
/// project declares them as content for the VSIX, and they have to exist on disk.
/// </summary>
public class VsixManifestTests
{
    private static readonly string ProjectDir = Path.Combine(FindRepoRoot(), "Src", "Obfy.VisualStudio");

    [Theory]
    [InlineData("License")]
    [InlineData("Icon")]
    [InlineData("PreviewImage")]
    public void ManifestFile_IsExistingContentIncludedInTheVsix(string element)
    {
        var manifest = XDocument.Load(Path.Combine(ProjectDir, "source.extension.vsixmanifest"));
        var reference = manifest.Descendants().Single(e => e.Name.LocalName == element).Value.Trim();

        var project = XDocument.Load(Path.Combine(ProjectDir, "Obfy.VisualStudio.csproj"));
        var content = project.Descendants()
            .Where(e => e.Name.LocalName == "Content")
            .Select(e => new
            {
                Include = (string?)e.Attribute("Include") ?? string.Empty,
                Link = e.Elements().FirstOrDefault(c => c.Name.LocalName == "Link")?.Value.Trim(),
                InVsix = string.Equals(
                    e.Elements().FirstOrDefault(c => c.Name.LocalName == "IncludeInVSIX")?.Value.Trim(),
                    "true",
                    StringComparison.OrdinalIgnoreCase)
            })
            .ToList();

        // The path inside the package is the link when there is one, otherwise the project-relative include.
        var item = content.SingleOrDefault(c => string.Equals(c.Link ?? c.Include, reference, StringComparison.OrdinalIgnoreCase));
        item.ShouldNotBeNull($"<{element}>{reference}</{element}> is not a <Content> item of Obfy.VisualStudio.csproj");
        item.InVsix.ShouldBeTrue($"{reference} must have <IncludeInVSIX>true</IncludeInVSIX>");

        var onDisk = Path.GetFullPath(Path.Combine(ProjectDir, item.Include));
        File.Exists(onDisk).ShouldBeTrue($"{reference} resolves to {onDisk}, which does not exist");
    }

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
