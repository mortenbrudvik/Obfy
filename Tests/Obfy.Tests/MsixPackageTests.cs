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

    [Fact]
    public void PackageLayout_IncludesManifestBuildScriptAndVersion()
    {
        File.Exists(ManifestPath).ShouldBeTrue($"Expected MSIX manifest at {ManifestPath}");
        File.Exists(BuildScriptPath).ShouldBeTrue($"Expected MSIX build script at {BuildScriptPath}");
        File.Exists(VersionPath).ShouldBeTrue($"Expected version.json at {VersionPath}");
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
        cli.Element(UapNs + "VisualElements")
            .ShouldNotBeNull()
            .Attribute("AppListEntry")!.Value.ShouldBe("none");

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
        var expected = new Dictionary<string, (int Width, int Height)>
        {
            ["StoreLogo.png"] = (50, 50),
            ["Square44x44Logo.png"] = (44, 44),
            ["Square71x71Logo.png"] = (71, 71),
            ["Square150x150Logo.png"] = (150, 150),
            ["Wide310x150Logo.png"] = (310, 150),
            ["SplashScreen.png"] = (620, 300),
        };

        foreach (var (fileName, size) in expected)
        {
            var path = Path.Combine(AssetsDir, fileName);
            File.Exists(path).ShouldBeTrue($"Expected MSIX asset {path}");
            ReadPngSize(path).ShouldBe(size, $"Asset {fileName} has the wrong pixel size");
        }
    }

    private static XElement LoadManifest()
    {
        File.Exists(ManifestPath).ShouldBeTrue($"Expected MSIX manifest at {ManifestPath}");
        return XDocument.Load(ManifestPath).Root.ShouldNotBeNull();
    }

    private static string ExpectedPackageVersion()
    {
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
