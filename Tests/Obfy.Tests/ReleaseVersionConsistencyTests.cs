using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Obfy.Core.Models;
using Shouldly;

namespace Obfy.Tests;

/// <summary>
/// <c>version.json</c> is the one place the product version is written. The .NET projects read it through
/// <c>Directory.Build.props</c>, the installer script and the release-tag check read it through
/// <c>build/Get-ProductVersion.ps1</c>, and the MSIX script reads it directly. The only copy that stays in a
/// file by hand is the Identity Version in <c>package/AppxManifest.xml</c>; a test below pins it, so bumping
/// <c>version.json</c> without the manifest fails the build.
/// </summary>
public class ReleaseVersionConsistencyTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    // Properties that set a product version. A project that states one of these as a plain value has a second
    // authority for the version.
    private static readonly string[] VersionProperties =
    [
        "Version", "VersionPrefix", "AssemblyVersion", "FileVersion", "InformationalVersion",
        "PackageVersion", "ApplicationVersion"
    ];

    [Fact]
    public void NoProjectOrBuildPropsFile_HardCodesAVersion()
    {
        var files = CentralPackageVersionTests.EnumerateOwnProjects(RepoRoot)
            .Concat(new[] { "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props" }
                .Select(name => Path.Combine(RepoRoot, name))
                .Where(File.Exists))
            .ToList();
        files.ShouldNotBeEmpty();

        var violations = files
            .SelectMany(file => HardCodedVersions(XDocument.Load(file))
                .Select(found => $"{Path.GetRelativePath(RepoRoot, file)}: {found}"))
            .ToList();

        violations.ShouldBeEmpty("the product version is written in version.json only; projects must not state it");
    }

    [Theory]
    [InlineData("<Version>3.0.0</Version>")]
    [InlineData("<Version Condition=\"'$(Version)' == ''\">3.0.0</Version>")]
    [InlineData("<AssemblyVersion Condition=\"'$(TargetFramework)' != ''\">3.0.0.0</AssemblyVersion>")]
    [InlineData("<InformationalVersion Label=\"x\">3.0.0-beta</InformationalVersion>")]
    [InlineData("<VersionPrefix>3.0.0</VersionPrefix>")]
    public void HardCodedVersionScan_CatchesALiteralOnAnElementWithOrWithoutAttributes(string property)
    {
        var project = XDocument.Parse($"<Project><PropertyGroup>{property}</PropertyGroup></Project>");

        HardCodedVersions(project).ShouldHaveSingleItem();
    }

    [Theory]
    [InlineData("<Version Condition=\"'$(Version)' == ''\">$(ObfyProductVersion)</Version>")]
    [InlineData("<Version>$(ObfyProductVersion)</Version>")]
    [InlineData("<TargetFramework>net10.0</TargetFramework>")]
    public void HardCodedVersionScan_AcceptsAVersionTakenFromAProperty(string property)
    {
        var project = XDocument.Parse($"<Project><PropertyGroup>{property}</PropertyGroup></Project>");

        HardCodedVersions(project).ShouldBeEmpty();
    }

    [Fact]
    public void HardCodedVersionScan_IgnoresPackageVersionItems()
    {
        // Directory.Packages.props lists dependency versions as items; those are not the product version.
        var packages = XDocument.Parse("<Project><ItemGroup><PackageVersion Include=\"dnlib\" Version=\"4.5.0\" /></ItemGroup></Project>");

        HardCodedVersions(packages).ShouldBeEmpty();
    }

    /// <summary>Properties of the document that state a version as plain text (no <c>$(...)</c> reference).</summary>
    private static IEnumerable<string> HardCodedVersions(XDocument document) =>
        document.Descendants()
            .Where(e => e.Parent?.Name.LocalName == "PropertyGroup" && VersionProperties.Contains(e.Name.LocalName))
            .Where(e => !e.Value.Contains("$(", StringComparison.Ordinal))
            .Select(e => $"<{e.Name.LocalName}>{e.Value.Trim()}</{e.Name.LocalName}>");

    [Fact]
    public void DirectoryBuildProps_ReadsTheVersionFromVersionJsonAndLetsTheCommandLineWin()
    {
        var props = File.ReadAllText(Path.Combine(RepoRoot, "Directory.Build.props"));

        props.ShouldContain("version.json");
        props.ShouldContain("ObfyProductVersion");
        // -p:Version=... (publish.yml passes the release tag) must keep overriding the file.
        props.ShouldContain("'$(Version)' == ''");
        props.ShouldContain("InitialTargets=");
    }

    [Theory]
    [InlineData("Src/Obfy.Console/Obfy.Console.csproj")]
    [InlineData("Src/Obfy.Core/Obfy.Core.csproj")]
    [InlineData("Src/Settings.Core/Settings.Core.csproj")]
    public void SrcProjects_EvaluateToTheVersionInVersionJson(string project)
    {
        EvaluateVersion(project).ShouldBe(VersionJsonVersion(), $"{project} Version");
    }

    [Fact]
    public void UiProject_EvaluatesToTheVersionInVersionJson()
    {
        // ObfyUI.exe is the desktop app (installer and MSIX). The WPF project targets Windows, so it is only
        // evaluated here; evaluation runs no targets, and SkipFrontendBuild keeps the npm steps out regardless.
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        EvaluateVersion("Src/Obfy.UI/Obfy.UI.csproj", "-p:SkipFrontendBuild=true").ShouldBe(VersionJsonVersion());
    }

    [Fact]
    public void SrcProject_ReachedThroughAPathInOtherCase_StillTakesTheVersionInVersionJson()
    {
        // Windows keeps the case a path was typed in (dotnet build .\src\...). A case-sensitive match on "Src" in
        // Directory.Build.props would skip the version silently and build 1.0.0. Needs a case-insensitive file system.
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        EvaluateVersion("src/Obfy.Core/Obfy.Core.csproj").ShouldBe(VersionJsonVersion());
    }

    [Theory]
    [InlineData("Src/Obfy.Console/Obfy.Console.csproj", "9.9.9")]
    [InlineData("Src/Obfy.Core/Obfy.Core.csproj", "2.5.0-beta.1")]
    public void CommandLineVersion_OverridesVersionJson(string project, string commandLineVersion)
    {
        EvaluateVersion(project, $"-p:Version={commandLineVersion}").ShouldBe(commandLineVersion);
    }

    // The native host stub embeds Obfy.PackedBootstrap, and its source stamp covers that project file; the VM
    // runtime is embedded into protected assemblies. Both keep their own (default) assembly version.
    [Theory]
    [InlineData("Src/Obfy.PackedBootstrap/Obfy.PackedBootstrap.csproj")]
    [InlineData("Src/Obfy.VmRuntime/Obfy.VmRuntime.csproj")]
    public void ProjectsWithTheirOwnVersionScheme_DoNotTakeTheProductVersion(string project)
    {
        EvaluateVersion(project).ShouldBe("1.0.0", $"{project} keeps the SDK default version");
    }

    [Fact]
    public void CoreAssembly_CarriesTheVersionFromVersionJson()
    {
        // Obfy.Core carries the product version: app.info reports it, ReportService prints it, and the
        // incremental cache and the VM seed key on it. A -p:Version prerelease suffix (release builds) is allowed.
        var expected = VersionJsonVersion();
        var assembly = typeof(ObfySettings).Assembly;

        var version = assembly.GetName().Version.ShouldNotBeNull();
        $"{version.Major}.{version.Minor}.{version.Build}".ShouldBe(expected);
        version.Revision.ShouldBe(0);

        var informational = assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), inherit: false)
            .Cast<System.Reflection.AssemblyInformationalVersionAttribute>()
            .Single().InformationalVersion;
        informational.ShouldStartWith(expected);
    }

    [Fact]
    public void ManifestSnapshot_MatchesVersionJson()
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot, "package", "AppxManifest.xml"));
        var match = Regex.Match(text, "<Identity[^>]*?\\sVersion=\"([^\"]+)\"", RegexOptions.Singleline);
        match.Success.ShouldBeTrue("AppxManifest.xml Identity has no Version");

        match.Groups[1].Value.ShouldBe(VersionJsonVersion() + ".0",
            "Identity Version in package/AppxManifest.xml (a snapshot; build-msix.ps1 re-stamps it from version.json)");
    }

    [Fact]
    public void InstallerScript_TakesTheVersionFromTheCompilerDefine_WithNoDefaultThatCouldDrift()
    {
        var iss = File.ReadAllText(Path.Combine(RepoRoot, "build", "ObfySetup.iss"));

        // No literal: a #define of the version (or a guarded default) is a second copy to bump.
        Regex.IsMatch(iss, @"^\s*#define\s+MyAppVersion\b", RegexOptions.Multiline)
            .ShouldBeFalse("ObfySetup.iss must not define MyAppVersion; build-installer.ps1 passes /DMyAppVersion");

        // Compiling without the define fails with a message that names the way to build.
        var guard = Regex.Match(iss, @"#ifndef\s+MyAppVersion\s*\r?\n\s*#error\s+([^\r\n]+)");
        guard.Success.ShouldBeTrue("ObfySetup.iss must stop with #error when MyAppVersion is not defined");
        guard.Groups[1].Value.ShouldContain("build-installer.ps1");
        guard.Groups[1].Value.ShouldContain("version.json");
        guard.Index.ShouldBeLessThan(iss.IndexOf("[Setup]", StringComparison.Ordinal));

        // The output name scheme is unchanged.
        iss.ShouldContain("AppVersion={#MyAppVersion}");
        iss.ShouldContain("AppVerName={#MyAppName} {#MyAppVersion}");
        iss.ShouldContain("OutputBaseFilename=ObfySetup-{#MyAppVersion}");
    }

    [Fact]
    public void InstallerBuildScript_ReadsVersionJsonAndPassesItToTheCompiler()
    {
        var script = File.ReadAllText(Path.Combine(RepoRoot, "build", "build-installer.ps1"));

        script.ShouldContain("/DMyAppVersion=");

        // The call itself (not the comment that mentions the script) comes before the long publish, so a bad
        // version.json fails fast, and the compiler is started after both.
        var call = Regex.Match(script, @"^\s*\$\w+\s*=\s*\(&\s*\(Join-Path \$BuildDir ""Get-ProductVersion\.ps1""\)", RegexOptions.Multiline);
        call.Success.ShouldBeTrue("build-installer.ps1 must run Get-ProductVersion.ps1 and keep its result");
        call.Index.ShouldBeLessThan(script.IndexOf("Publishing Obfy CLI", StringComparison.Ordinal));
        call.Index.ShouldBeLessThan(script.IndexOf("/DMyAppVersion=", StringComparison.Ordinal));
    }

    [Fact]
    public void PublishWorkflow_ChecksTheTagAgainstVersionJson_AndStillPassesTheVersionToBuildAndPack()
    {
        var workflow = File.ReadAllText(Path.Combine(RepoRoot, ".github", "workflows", "publish.yml"));

        workflow.ShouldContain("Resolve-PackageVersion.ps1 -RefName $env:PACKAGE_REF -VersionFile ./version.json");
        // The tag may carry a prerelease suffix that version.json cannot, so it stays the package version.
        Regex.Matches(workflow, Regex.Escape("-p:Version=${{ steps.version.outputs.version }}")).Count
            .ShouldBe(2, "dotnet build and dotnet pack both pass the resolved version");
    }

    [Fact]
    public void PublishWorkflow_ChecksTheTagRightAfterCheckout_BeforeAnySlowSetup()
    {
        var workflow = File.ReadAllText(Path.Combine(RepoRoot, ".github", "workflows", "publish.yml"));

        var checkout = workflow.IndexOf("uses: actions/checkout@", StringComparison.Ordinal);
        var resolve = workflow.IndexOf("- name: Resolve package version", StringComparison.Ordinal);
        checkout.ShouldBeGreaterThanOrEqualTo(0);
        resolve.ShouldBeGreaterThan(checkout);

        // A tag that disagrees with version.json fails before the .NET and Node setup and the native-host rebuild.
        foreach (var slowStep in new[] { "- name: Setup .NET", "- name: Setup MSVC", "- name: Rebuild native packer host", "- name: Setup Node", "- name: Restore", "- name: Build" })
        {
            var index = workflow.IndexOf(slowStep, StringComparison.Ordinal);
            index.ShouldBeGreaterThanOrEqualTo(0, slowStep);
            resolve.ShouldBeLessThan(index, $"'Resolve package version' must run before '{slowStep}'");
        }

        // Only the checkout lies between the start of the steps and the check.
        var stepsStart = workflow.IndexOf("    steps:", StringComparison.Ordinal);
        stepsStart.ShouldBeGreaterThanOrEqualTo(0);
        Regex.Matches(workflow[stepsStart..resolve], @"^\s*- (name|uses):", RegexOptions.Multiline).Count
            .ShouldBe(1, "the checkout is the only step before the version check");
    }

    private static string VersionJsonVersion()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot, "version.json")));
        var root = document.RootElement;
        return $"{root.GetProperty("major").GetInt32()}.{root.GetProperty("minor").GetInt32()}.{root.GetProperty("patch").GetInt32()}";
    }

    /// <summary>The Version MSBuild evaluates for a project, without building it.</summary>
    private static string EvaluateVersion(string relativeProject, params string[] extraArguments) =>
        MsBuildEvaluation.Property(
            RepoRoot,
            Path.Combine(RepoRoot, relativeProject.Replace('/', Path.DirectorySeparatorChar)),
            "Version",
            extraArguments);

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
