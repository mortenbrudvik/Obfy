using Shouldly;

namespace Obfy.Tests;

/// <summary>
/// Runs the real Directory.Build.props against a throw-away tree (a copy of the props file, a version.json of the
/// test's choosing and a minimal SDK project) so every shape of version.json and every folder name can be tried
/// without touching the repository.
/// </summary>
public class DirectoryBuildPropsVersionTests : IDisposable
{
    private static readonly string RepoRoot = FindRepoRoot();
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ObfyPropsProbe_{Guid.NewGuid():N}");

    public DirectoryBuildPropsVersionTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    [Theory]
    [MemberData(nameof(VersionJsonCases.Accepted), MemberType = typeof(VersionJsonCases))]
    public void AcceptedVersionJson_BecomesTheVersionOfASrcProject(string json, string expected)
    {
        var project = CreateTree(json, Path.Combine("Src", "Probe"));

        MsBuildEvaluation.Property(_root, project, "Version").ShouldBe(expected);
    }

    [Theory]
    [MemberData(nameof(VersionJsonCases.Rejected), MemberType = typeof(VersionJsonCases))]
    public void RejectedVersionJson_YieldsNoProductVersion(string json)
    {
        var project = CreateTree(json, Path.Combine("Src", "Probe"));

        MsBuildEvaluation.Property(_root, project, "ObfyProductVersion").ShouldBeEmpty();
    }

    [Theory]
    [InlineData("{\"previous\":{\"major\":1,\"minor\":9,\"patch\":9},\"major\":2,\"minor\":0,\"patch\":0}")]
    [InlineData("{ \"major\": 2, \"minor\": 0 }")]
    public void RejectedVersionJson_FailsTheBuildWithAMessageThatNamesTheFile(string json)
    {
        var project = CreateTree(json, Path.Combine("Src", "Probe"));

        var (exitCode, output) = MsBuildEvaluation.Run(_root, project, "-t:ObfyRequireProductVersion");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("version.json");
        output.ShouldContain("integer");
    }

    [Fact]
    public void MissingVersionJson_FailsTheBuildWithAMessageThatNamesTheFile()
    {
        var project = CreateTree(json: null, Path.Combine("Src", "Probe"));

        var (exitCode, output) = MsBuildEvaluation.Run(_root, project, "-t:ObfyRequireProductVersion");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("version.json");
    }

    [Fact]
    public void BothReadersSpellTheNumberRuleTheSameWay()
    {
        // The behaviour tests above and in ProductVersionScriptTests feed both readers the same files; this pins
        // that the literal rule (0..65535, no leading zeros) was not edited in one place only.
        const string number = @"0|[1-9]\d{0,3}|[1-5]\d{4}|6[0-4]\d{3}|65[0-4]\d{2}|655[0-2]\d|6553[0-5]";

        File.ReadAllText(Path.Combine(RepoRoot, "Directory.Build.props")).ShouldContain(number);
        File.ReadAllText(Path.Combine(RepoRoot, "build", "Get-ProductVersion.ps1")).ShouldContain(number);
    }

    [Fact]
    public void ProjectsOutsideSrc_KeepTheSdkDefaultVersion()
    {
        var project = CreateTree("{ \"major\": 7, \"minor\": 8, \"patch\": 9 }", Path.Combine("Tests", "Probe"));

        MsBuildEvaluation.Property(_root, project, "Version").ShouldBe("1.0.0");
    }

    [Theory]
    [InlineData("SrcGen")]
    [InlineData("Src.Old")]
    [InlineData("Srcs")]
    public void ASiblingFolderThatOnlyStartsWithSrc_DoesNotTakeTheProductVersion(string folder)
    {
        var project = CreateTree("{ \"major\": 7, \"minor\": 8, \"patch\": 9 }", Path.Combine(folder, "Probe"));

        MsBuildEvaluation.Property(_root, project, "Version").ShouldBe("1.0.0");
    }

    [Fact]
    public void ASrcProjectReachedThroughAPathInOtherCase_StillTakesTheProductVersion()
    {
        // Windows keeps the case a path was typed in (dotnet build .\src\...). Needs a case-insensitive file system.
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        CreateTree("{ \"major\": 7, \"minor\": 8, \"patch\": 9 }", Path.Combine("Src", "Probe"));
        var typedInLowerCase = Path.Combine(_root, "src", "Probe", "Probe.csproj");

        MsBuildEvaluation.Property(_root, typedInLowerCase, "Version").ShouldBe("7.8.9");
    }

    [Theory]
    [InlineData("Obfy.VisualStudio")]
    [InlineData("Obfy.PackedBootstrap")]
    [InlineData("Obfy.VmRuntime")]
    public void SrcProjectsWithTheirOwnVersionScheme_KeepTheSdkDefaultVersion(string projectName)
    {
        var project = CreateTree("{ \"major\": 7, \"minor\": 8, \"patch\": 9 }", Path.Combine("Src", projectName), projectName);

        MsBuildEvaluation.Property(_root, project, "Version").ShouldBe("1.0.0");
    }

    [Fact]
    public void ACommandLineVersion_WinsOverVersionJson()
    {
        var project = CreateTree("{ \"major\": 7, \"minor\": 8, \"patch\": 9 }", Path.Combine("Src", "Probe"));

        MsBuildEvaluation.Property(_root, project, "Version", "-p:Version=2.1.0-beta.1").ShouldBe("2.1.0-beta.1");
    }

    /// <summary>
    /// Copies Directory.Build.props and writes version.json (unless <paramref name="json"/> is null) into the probe
    /// root, then a minimal project at <c>root/directory/name.csproj</c>. Returns the project path.
    /// </summary>
    private string CreateTree(string? json, string directory, string name = "Probe")
    {
        File.Copy(Path.Combine(RepoRoot, "Directory.Build.props"), Path.Combine(_root, "Directory.Build.props"), overwrite: true);
        if (json is not null)
        {
            File.WriteAllText(Path.Combine(_root, "version.json"), json);
        }

        var projectDirectory = Path.Combine(_root, directory);
        Directory.CreateDirectory(projectDirectory);
        var project = Path.Combine(projectDirectory, name + ".csproj");
        File.WriteAllText(project, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        return project;
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
