using System.IO;
using Shouldly;

namespace Obfy.UI.AutomationTests;

/// <summary>
/// The harness learns the debug port from the file the browser writes (<c>--remote-debugging-port=0</c>). It reads the
/// file while the browser may still be writing it, so a missing, empty or half-written file must read as "not yet".
/// </summary>
public class DevToolsPortFileTests : IDisposable
{
    private readonly string _work = Path.Combine(Path.GetTempPath(), "obfy-port-" + Guid.NewGuid().ToString("N"));

    public DevToolsPortFileTests() => Directory.CreateDirectory(_work);

    public void Dispose()
    {
        try { Directory.Delete(_work, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public void PortFile_LivesInTheEmbeddedBrowsersFolderOfTheLaunchProfile()
    {
        UiLaunch.DevToolsPortFile(_work)
            .ShouldBe(Path.Combine(_work, "webview2", "EBWebView", "DevToolsActivePort"));
    }

    [Fact]
    public void ReadPort_ReturnsNullWhileTheFileIsMissing()
    {
        ObfyApp.ReadPort(UiLaunch.DevToolsPortFile(_work)).ShouldBeNull();
    }

    [Theory]
    [InlineData("49458\n/devtools/browser/853c6b88-927d-4d08-bdec-4b3a3309caea", 49458)]
    [InlineData("49458\r\n/devtools/browser/x\r\n", 49458)]
    [InlineData("49458", 49458)]
    [InlineData("", null)]
    [InlineData("\n/devtools/browser/x", null)]
    [InlineData("0\n/devtools/browser/x", null)]
    [InlineData("49", 49)]
    [InlineData("not a port", null)]
    public void ReadPort_ReadsTheFirstLine(string content, int? expected)
    {
        var file = UiLaunch.DevToolsPortFile(_work);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, content);

        ObfyApp.ReadPort(file).ShouldBe(expected);
    }

    [Fact]
    public void ReadPort_ReadsAFileThatTheBrowserStillHasOpen()
    {
        var file = UiLaunch.DevToolsPortFile(_work);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        using var writer = new FileStream(file, FileMode.Create, FileAccess.Write, FileShare.Read);
        writer.Write("51234\n/devtools/browser/x"u8);
        writer.Flush();

        ObfyApp.ReadPort(file).ShouldBe(51234);
    }

    [Fact]
    public void BrowserArguments_AskForAnyFreePortAndAllowTheTestsOrigin()
    {
        UiLaunch.BrowserArguments.ShouldContain("--remote-debugging-port=0");
        UiLaunch.BrowserArguments.ShouldContain("--remote-allow-origins=*");
    }
}
