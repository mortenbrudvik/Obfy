using Shouldly;

namespace Obfy.Tests;

/// <summary>
/// Static-text pins for the packaging scripts: the UI is a WebView2 host, so the Inno installer must
/// ensure the Evergreen runtime and both packages must carry the web <c>Assets\</c> without symbols.
/// </summary>
public class InstallerWebView2Tests
{
    private const string EvergreenClientGuid = "{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";

    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly string IssPath = Path.Combine(RepoRoot, "build", "ObfySetup.iss");
    private static readonly string InstallerScriptPath = Path.Combine(RepoRoot, "build", "build-installer.ps1");
    private static readonly string MsixScriptPath = Path.Combine(RepoRoot, "build", "build-msix.ps1");
    private static readonly string ManifestPath = Path.Combine(RepoRoot, "package", "AppxManifest.xml");

    [Fact]
    public void Iss_ChecksWebView2RuntimeRegistration()
    {
        var iss = File.ReadAllText(IssPath);

        iss.ShouldContain(EvergreenClientGuid);
        iss.ShouldContain("function IsWebView2RuntimeInstalled");
        iss.ShouldContain(@"SOFTWARE\Microsoft\EdgeUpdate\Clients\");
        iss.ShouldContain(@"SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\");
        iss.ShouldContain("'pv'");
        iss.ShouldContain("HKLM");
        iss.ShouldContain("HKCU");
    }

    [Fact]
    public void Iss_RunsBundledBootstrapperSilentlyWhenRuntimeIsAbsent()
    {
        var iss = File.ReadAllText(IssPath);

        iss.ShouldContain("MicrosoftEdgeWebview2Setup.exe");
        iss.ShouldContain("Flags: dontcopy noencryption");
        iss.ShouldContain("ExtractTemporaryFile('MicrosoftEdgeWebview2Setup.exe')");
        iss.ShouldContain("'/silent /install'");
        iss.ShouldContain("if not IsWebView2RuntimeInstalled then");
        iss.ShouldContain("function PrepareToInstall");
    }

    [Fact]
    public void Iss_StillInstallsUiPublishOutputAndLaunchesObfyUi()
    {
        var iss = File.ReadAllText(IssPath);

        iss.ShouldContain(@"Source: "".\publish-ui\*""; DestDir: ""{app}\UI""");
        iss.ShouldContain("#define MyAppUIExeName \"ObfyUI.exe\"");
        iss.ShouldContain(@"Filename: ""{app}\UI\{#MyAppUIExeName}""");
        iss.ShouldContain("/f /im {#MyAppUIExeName}");
    }

    [Fact]
    public void Iss_ExcludesSymbolsAndSourceMapsFromUiPayload()
    {
        var iss = File.ReadAllText(IssPath);
        var uiLine = iss.Split('\n').Single(l => l.Contains(@".\publish-ui\*"));

        uiLine.ShouldContain("Excludes: \"*.pdb,*.map\"");
    }

    [Fact]
    public void InstallerScript_PublishesUiWithFrontendAndAssertsAssets()
    {
        var script = File.ReadAllText(InstallerScriptPath);

        script.ShouldNotContain("SkipFrontendBuild");
        script.ShouldContain(@"Assets\index.html");
        script.ShouldContain("frontend did not build");
    }

    [Fact]
    public void InstallerScript_DownloadsAndVerifiesWebView2Bootstrapper()
    {
        var script = File.ReadAllText(InstallerScriptPath);

        script.ShouldContain("https://go.microsoft.com/fwlink/p/?LinkId=2124703");
        script.ShouldContain("MicrosoftEdgeWebview2Setup.exe");
        script.ShouldContain("redist");
        script.ShouldContain("Get-AuthenticodeSignature");
        script.ShouldContain("O=Microsoft Corporation");
    }

    [Fact]
    public void MsixScript_KeepsWebAssetsAndStoreLogosTogetherInAssets()
    {
        var script = File.ReadAllText(MsixScriptPath);

        // The web build lands in Assets\ via the UI publish, then the Store logos are merged into the same folder.
        // Copying the logo *folder* onto an existing Assets\ would nest it as Assets\Assets\ and break the manifest paths.
        script.ShouldContain(@"Assets\index.html");
        script.ShouldContain("Join-Path $AssetsSource \"*\"");
        script.ShouldNotContain("Copy-Item $AssetsSource $layoutAssets");
    }

    [Fact]
    public void MsixScript_FailsWhenSourceMapsRemainAndDoesNotBundleWebView2()
    {
        var script = File.ReadAllText(MsixScriptPath);

        script.ShouldContain("*.map");
        script.ShouldContain("Source maps remain");
        script.ShouldNotContain("MicrosoftEdgeWebview2Setup");
        File.ReadAllText(ManifestPath).ShouldNotContain("WebView2", Case.Insensitive);
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
