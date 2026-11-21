using System.Text.Json;
using System.Text.RegularExpressions;
using Shouldly;

namespace Obfy.Tests;

/// <summary>
/// The product version is stated in several places that are edited by hand and shipped together
/// (version.json, the CLI and UI projects, the Inno Setup script, the MSIX manifest). Nothing derives
/// one from another, so a release that bumps only some of them ships mismatched installers and packages.
/// </summary>
public class ReleaseVersionConsistencyTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    [Fact]
    public void AllVersionDeclarations_AgreeWithVersionJson()
    {
        var expected = VersionJsonVersion();

        ProjectVersion("Src/Obfy.Console/Obfy.Console.csproj").ShouldBe(expected, "Obfy.Console.csproj <Version>");
        ProjectVersion("Src/Obfy.UI/Obfy.UI.csproj").ShouldBe(expected, "Obfy.UI.csproj <Version>");
        InstallerVersion().ShouldBe(expected, "MyAppVersion in build/ObfySetup.iss");
        ManifestVersion().ShouldBe(expected + ".0", "Identity Version in package/AppxManifest.xml");
    }

    private static string VersionJsonVersion()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot, "version.json")));
        var root = document.RootElement;
        return $"{root.GetProperty("major").GetInt32()}.{root.GetProperty("minor").GetInt32()}.{root.GetProperty("patch").GetInt32()}";
    }

    private static string ProjectVersion(string relative)
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
        var match = Regex.Match(text, @"<Version>\s*([^<\s]+)\s*</Version>");
        match.Success.ShouldBeTrue($"{relative} has no <Version>");
        return match.Groups[1].Value;
    }

    private static string InstallerVersion()
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot, "build", "ObfySetup.iss"));
        var match = Regex.Match(text, "^#define MyAppVersion \"([^\"]+)\"", RegexOptions.Multiline);
        match.Success.ShouldBeTrue("ObfySetup.iss has no #define MyAppVersion");
        return match.Groups[1].Value;
    }

    private static string ManifestVersion()
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot, "package", "AppxManifest.xml"));
        var match = Regex.Match(text, "<Identity[^>]*?\\sVersion=\"([^\"]+)\"", RegexOptions.Singleline);
        match.Success.ShouldBeTrue("AppxManifest.xml Identity has no Version");
        return match.Groups[1].Value;
    }

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
