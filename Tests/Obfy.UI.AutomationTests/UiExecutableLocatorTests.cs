using System.IO;
using Shouldly;

namespace Obfy.UI.AutomationTests;

public class UiExecutableLocatorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "obfy-ui-loc-" + Guid.NewGuid().ToString("N"));
    private const string Tfm = "net10.0-windows10.0.26100";

    public UiExecutableLocatorTests()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "Obfy.sln"), "");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public void DetectConfiguration_ReadsReleaseFromBaseDirectory()
    {
        var baseDir = Path.Combine(_root, "Tests", "Obfy.UI.AutomationTests", "bin", "Release", Tfm);
        UiExecutableLocator.DetectConfiguration(baseDir).ShouldBe("Release");
    }

    [Fact]
    public void DetectConfiguration_DefaultsToDebugWhenReleaseSegmentMissing()
    {
        var baseDir = Path.Combine(_root, "Tests", "Obfy.UI.AutomationTests", "bin", "Debug", Tfm);
        UiExecutableLocator.DetectConfiguration(baseDir).ShouldBe("Debug");
    }

    [Fact]
    public void Resolve_PrefersHintedConfigurationWhenBothExist()
    {
        PlaceExe("Debug");
        PlaceExe("Release");

        var path = UiExecutableLocator.Resolve(_root, Tfm, "Release");

        path.ShouldBe(ExpectedPath("Release"));
    }

    [Fact]
    public void Resolve_FallsBackToTheOtherConfiguration()
    {
        PlaceExe("Debug");

        var path = UiExecutableLocator.Resolve(_root, Tfm, "Release");

        path.ShouldBe(ExpectedPath("Debug"));
    }

    [Fact]
    public void Resolve_ThrowsWhenNeitherConfigurationExists()
    {
        var ex = Should.Throw<FileNotFoundException>(
            () => UiExecutableLocator.Resolve(_root, Tfm, "Release"));

        ex.Message.ShouldContain("ObfyUI.exe");
        ex.Message.ShouldContain(ExpectedPath("Release"));
        ex.Message.ShouldContain(ExpectedPath("Debug"));
    }

    [Fact]
    public void ResolveFromTestContext_UsesReleaseLayoutWhenTestsWereBuiltRelease()
    {
        PlaceExe("Release");
        var baseDir = Path.Combine(_root, "Tests", "Obfy.UI.AutomationTests", "bin", "Release", Tfm);
        Directory.CreateDirectory(baseDir);

        var path = UiExecutableLocator.ResolveFromTestContext(baseDir);

        path.ShouldBe(ExpectedPath("Release"));
    }

    private void PlaceExe(string configuration)
    {
        var dir = Path.Combine(_root, "Src", "Obfy.UI", "bin", configuration, Tfm);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "ObfyUI.exe"), "");
    }

    private string ExpectedPath(string configuration) =>
        Path.Combine(_root, "Src", "Obfy.UI", "bin", configuration, Tfm, "ObfyUI.exe");
}
