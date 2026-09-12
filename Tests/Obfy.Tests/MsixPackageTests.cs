using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using Shouldly;

namespace Obfy.Tests;

public class MsixPackageTests
{
    private static readonly XNamespace ManifestNs = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
    private static readonly XNamespace UapNs = "http://schemas.microsoft.com/appx/manifest/uap/windows10";
    private static readonly XNamespace Uap5Ns = "http://schemas.microsoft.com/appx/manifest/uap/windows10/5";
    private static readonly XNamespace RescapNs = "http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities";

    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly string ManifestPath = Path.Combine(RepoRoot, "package", "AppxManifest.xml");
    private static readonly string AssetsDir = Path.Combine(RepoRoot, "package", "Assets");
    private static readonly string VersionPath = Path.Combine(RepoRoot, "version.json");
    private static readonly string BuildScriptPath = Path.Combine(RepoRoot, "build", "build-msix.ps1");
    private static readonly string AssetGeneratorPath = Path.Combine(RepoRoot, "build", "generate-msix-assets.ps1");
    private static readonly string UiProjectPath = Path.Combine(RepoRoot, "Src", "Obfy.UI", "Obfy.UI.csproj");
    private static readonly string ConsoleProjectPath = Path.Combine(RepoRoot, "Src", "Obfy.Console", "Obfy.Console.csproj");
    private static readonly string SourceIconPath = Path.Combine(RepoRoot, "Src", "Obfy.UI", "Images", "app.png");

    private static readonly Dictionary<string, (int Width, int Height)> RequiredAssets = new()
    {
        ["StoreLogo.png"] = (50, 50),
        ["Square44x44Logo.png"] = (44, 44),
        ["Square71x71Logo.png"] = (71, 71),
        ["Square150x150Logo.png"] = (150, 150),
        ["Wide310x150Logo.png"] = (310, 150),
        ["SplashScreen.png"] = (620, 300),
    };

    [Fact]
    public void PackageLayout_IncludesManifestBuildScriptAndVersion()
    {
        File.Exists(ManifestPath).ShouldBeTrue($"Expected MSIX manifest at {ManifestPath}");
        File.Exists(BuildScriptPath).ShouldBeTrue($"Expected MSIX build script at {BuildScriptPath}");
        File.Exists(AssetGeneratorPath).ShouldBeTrue($"Expected MSIX asset generator at {AssetGeneratorPath}");
        File.Exists(VersionPath).ShouldBeTrue($"Expected version.json at {VersionPath}");
        File.Exists(SourceIconPath).ShouldBeTrue($"Expected source icon at {SourceIconPath}");
    }

    [Fact]
    public void Manifest_DeclaresDesktopFullTrustIdentity()
    {
        var root = LoadManifest();
        var identity = root.Element(ManifestNs + "Identity").ShouldNotBeNull();

        identity.Attribute("Name")!.Value.ShouldBe("Obfy.Obfy");
        identity.Attribute("Publisher")!.Value.ShouldBe("CN=Obfy");
        identity.Attribute("ProcessorArchitecture")!.Value.ShouldBe("x64");
        identity.Attribute("Version")!.Value.ShouldBe(ExpectedPackageVersion());

        var deviceFamily = root
            .Element(ManifestNs + "Dependencies")
            .ShouldNotBeNull()
            .Element(ManifestNs + "TargetDeviceFamily")
            .ShouldNotBeNull();

        deviceFamily.Attribute("Name")!.Value.ShouldBe("Windows.Desktop");
        Version.Parse(deviceFamily.Attribute("MinVersion")!.Value)
            .ShouldBeGreaterThanOrEqualTo(new Version(10, 0, 19041, 0));

        var properties = root.Element(ManifestNs + "Properties").ShouldNotBeNull();
        properties.Element(ManifestNs + "DisplayName").ShouldNotBeNull().Value.ShouldBe("Obfy");
        properties.Element(ManifestNs + "PublisherDisplayName").ShouldNotBeNull().Value.ShouldBe("Obfy");
        properties.Element(ManifestNs + "Logo").ShouldNotBeNull().Value.ShouldBe("Assets\\StoreLogo.png");

        root.Element(ManifestNs + "Capabilities")
            .ShouldNotBeNull()
            .Elements(RescapNs + "Capability")
            .Select(c => c.Attribute("Name")?.Value)
            .ShouldContain("runFullTrust");
    }

    [Fact]
    public void Manifest_ExposesUiEntryPointAndCliAlias()
    {
        var applications = LoadManifest()
            .Element(ManifestNs + "Applications")
            .ShouldNotBeNull()
            .Elements(ManifestNs + "Application")
            .ToList();

        var ui = applications.Where(a => a.Attribute("Id")?.Value == "Obfy").ShouldHaveSingleItem();
        ui.Attribute("Executable")!.Value.ShouldBe("ObfyUI.exe");
        ui.Attribute("EntryPoint")!.Value.ShouldBe("Windows.FullTrustApplication");

        var visual = ui.Element(UapNs + "VisualElements").ShouldNotBeNull();
        visual.Attribute("DisplayName")!.Value.ShouldBe("Obfy");
        visual.Attribute("Square150x150Logo")!.Value.ShouldBe("Assets\\Square150x150Logo.png");
        visual.Attribute("Square44x44Logo")!.Value.ShouldBe("Assets\\Square44x44Logo.png");

        var cli = applications.Where(a => a.Attribute("Id")?.Value == "ObfyCLI").ShouldHaveSingleItem();
        cli.Attribute("Executable")!.Value.ShouldBe("CLI\\obfy.exe");
        cli.Attribute("EntryPoint")!.Value.ShouldBe("Windows.FullTrustApplication");
        var cliVisual = cli.Element(UapNs + "VisualElements").ShouldNotBeNull();
        cliVisual.Attribute("AppListEntry")!.Value.ShouldBe("none");
        cliVisual.Attribute("Square150x150Logo")!.Value.ShouldBe("Assets\\Square150x150Logo.png");
        cliVisual.Attribute("Square44x44Logo")!.Value.ShouldBe("Assets\\Square44x44Logo.png");

        var alias = cli
            .Element(ManifestNs + "Extensions")
            .ShouldNotBeNull()
            .Element(Uap5Ns + "Extension")
            .ShouldNotBeNull();

        alias.Attribute("Category")!.Value.ShouldBe("windows.appExecutionAlias");
        alias.Element(Uap5Ns + "AppExecutionAlias")
            .ShouldNotBeNull()
            .Element(Uap5Ns + "ExecutionAlias")
            .ShouldNotBeNull()
            .Attribute("Alias")!.Value.ShouldBe("obfy.exe");
    }

    [Fact]
    public void PackageAssets_ExistAtRequiredStoreSizes()
    {
        foreach (var (fileName, size) in RequiredAssets)
        {
            var path = Path.Combine(AssetsDir, fileName);
            File.Exists(path).ShouldBeTrue($"Expected MSIX asset {path}");
            ReadPngSize(path).ShouldBe(size, $"Asset {fileName} has the wrong pixel size");
        }
    }

    [Fact]
    public void Manifest_AssetPathsExistOnDisk()
    {
        var root = LoadManifest();
        var paths = root.Descendants()
            .SelectMany(el => el.Attributes())
            .Where(attr => IsAssetPathAttribute(attr.Name.LocalName))
            .Select(attr => attr.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        paths.ShouldNotBeEmpty();
        foreach (var relative in paths)
        {
            var fileName = Path.GetFileName(relative.Replace('\\', Path.DirectorySeparatorChar));
            File.Exists(Path.Combine(AssetsDir, fileName))
                .ShouldBeTrue($"Manifest references missing asset '{relative}'");
            RequiredAssets.Keys.ShouldContain(fileName);
        }
    }

    [Fact]
    public void Manifest_ExecutablesMatchCsprojAssemblyNames()
    {
        ReadAssemblyName(UiProjectPath).ShouldBe("ObfyUI");
        ReadAssemblyName(ConsoleProjectPath).ShouldBe("obfy");

        var applications = LoadManifest()
            .Element(ManifestNs + "Applications")
            .ShouldNotBeNull()
            .Elements(ManifestNs + "Application")
            .ToList();

        applications.Single(a => a.Attribute("Id")?.Value == "Obfy")
            .Attribute("Executable")!.Value.ShouldBe("ObfyUI.exe");
        applications.Single(a => a.Attribute("Id")?.Value == "ObfyCLI")
            .Attribute("Executable")!.Value.ShouldBe("CLI\\obfy.exe");
    }

    [Fact]
    public void BuildScript_DoesNotWipeLayoutWhenSkipPublish()
    {
        var step2 = ScriptSection("[2/7]", "[3/7]");
        step2.ShouldContain("-not $SkipPublish", Case.Insensitive);
        step2.ShouldContain("Remove-Item $LayoutDir");
        step2.ShouldContain("CLI\\obfy.exe");
        step2.ShouldContain("ObfyUI.exe");
    }

    [Fact]
    public void BuildScript_TreatsMissingSignToolAsErrorUnlessSkipSign()
    {
        var script = ReadBuildScript();
        script.ShouldContain("if (-not $SkipSign -and -not $SignTool)");
        script.ShouldContain("or re-run with -SkipSign");
        script.ShouldNotContain("package will be unsigned");
    }

    [Fact]
    public void BuildScript_DoesNotTreatCurrentUserStoreAsSideloadTrust()
    {
        var script = ReadBuildScript();
        script.ShouldNotContain("CurrentUser\\TrustedPeople");
        script.ShouldContain("LocalMachine\\TrustedPeople");
    }

    [Fact]
    public void BuildScript_PublishesWinX64SelfContainedWithoutObfuscation()
    {
        var script = ReadBuildScript();
        script.ShouldContain("win-x64");
        script.ShouldContain("--self-contained");
        script.ShouldContain("CLI");
        script.ShouldNotContain("self-obfuscation");
        script.ShouldNotContain("obfuscated-ui");
        script.ShouldNotContain("obfuscated-cli");
    }

    [Fact]
    public void BuildScript_StampsAndValidatesVersionFromVersionJson()
    {
        var script = ReadBuildScript();
        script.ShouldContain("version.json");
        script.ShouldContain("0..65535");
        script.ShouldContain("$($version.major).$($version.minor).$($version.patch).0");
        script.ShouldContain("missing Package/Identity");
    }

    [Fact]
    public void BuildScript_FailsClosedOnMakePriUnlessSkipPri()
    {
        var script = ReadBuildScript();
        script.ShouldContain("[switch]$SkipPri");
        script.ShouldContain("makepri.exe not found");
        script.ShouldContain("resources.pri was not created");
        script.ShouldNotContain("packing without resources.pri");
        script.ShouldNotContain("continuing without resources.pri");
    }

    [Fact]
    public void BuildScript_AssertsPdbsAreRemovedAndRequiredAssetsExist()
    {
        var script = ReadBuildScript();
        script.ShouldContain("PDB files remain");
        foreach (var fileName in RequiredAssets.Keys)
        {
            script.ShouldContain(fileName);
        }
    }

    [Fact]
    public void BuildScript_ParsesSdkFolderVersionsWithoutSilentlyContinue()
    {
        var script = ReadBuildScript();
        script.ShouldContain("[version]::TryParse");
        script.ShouldNotContain("[version]($_.Name");
    }

    [Fact]
    public void AssetGenerator_RequiresStaAndWpfAssemblies()
    {
        var script = File.ReadAllText(AssetGeneratorPath);
        script.ShouldContain("GetApartmentState");
        script.ShouldContain("STA");
        script.ShouldContain("PresentationCore");
        script.ShouldContain("WPF assemblies are unavailable");
    }

    private static XElement LoadManifest()
    {
        File.Exists(ManifestPath).ShouldBeTrue($"Expected MSIX manifest at {ManifestPath}");
        return XDocument.Load(ManifestPath).Root.ShouldNotBeNull();
    }

    private static string ReadBuildScript() => File.ReadAllText(BuildScriptPath);

    private static string ScriptSection(string startMarker, string endMarker)
    {
        var script = ReadBuildScript();
        var start = script.IndexOf(startMarker, StringComparison.Ordinal);
        start.ShouldBeGreaterThanOrEqualTo(0, $"Expected '{startMarker}' in build-msix.ps1");
        var end = script.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        end.ShouldBeGreaterThan(start, $"Expected '{endMarker}' after '{startMarker}' in build-msix.ps1");
        return script[start..end];
    }

    private static bool IsAssetPathAttribute(string localName) =>
        localName.Equals("Logo", StringComparison.OrdinalIgnoreCase)
        || localName.EndsWith("Logo", StringComparison.OrdinalIgnoreCase)
        || localName.Equals("Image", StringComparison.OrdinalIgnoreCase);

    private static string ReadAssemblyName(string csprojPath)
    {
        File.Exists(csprojPath).ShouldBeTrue($"Expected project at {csprojPath}");
        var name = XDocument.Load(csprojPath).Descendants("AssemblyName").FirstOrDefault()?.Value;
        name.ShouldNotBeNullOrWhiteSpace($"Expected AssemblyName in {csprojPath}");
        return name!;
    }

    private static string ExpectedPackageVersion()
    {
        File.Exists(VersionPath).ShouldBeTrue($"Expected version.json at {VersionPath}");
        using var stream = File.OpenRead(VersionPath);
        var version = JsonSerializer.Deserialize<VersionJson>(stream, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }).ShouldNotBeNull();
        return string.Create(CultureInfo.InvariantCulture, $"{version.Major}.{version.Minor}.{version.Patch}.0");
    }

    private static (int Width, int Height) ReadPngSize(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        var signature = reader.ReadBytes(8);
        signature.ShouldBe(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });

        var chunkLength = ReadBigEndianInt32(reader);
        var chunkType = System.Text.Encoding.ASCII.GetString(reader.ReadBytes(4));
        chunkLength.ShouldBeGreaterThanOrEqualTo(8);
        chunkType.ShouldBe("IHDR");

        var width = ReadBigEndianInt32(reader);
        var height = ReadBigEndianInt32(reader);
        return (width, height);
    }

    private static int ReadBigEndianInt32(BinaryReader reader)
    {
        var bytes = reader.ReadBytes(4);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(bytes);
        }

        return BitConverter.ToInt32(bytes, 0);
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

    private sealed class VersionJson
    {
        public int Major { get; set; }
        public int Minor { get; set; }
        public int Patch { get; set; }
    }
}
