using Obfy.VisualStudio.Services;
using Shouldly;

namespace Obfy.VisualStudio.Tests;

public class InPlaceOutputCopyTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "obfy-inplace-" + Guid.NewGuid().ToString("N"));

    public InPlaceOutputCopyTests() => Directory.CreateDirectory(_dir);

    [Fact]
    public void Copy_ReplacesPrimaryAndCopiesSidecars()
    {
        var destDir = Path.Combine(_dir, "dest");
        var tempDir = Path.Combine(_dir, "temp");
        Directory.CreateDirectory(destDir);
        Directory.CreateDirectory(tempDir);

        var dest = Path.Combine(destDir, "App.dll");
        var tempOutput = Path.Combine(tempDir, "App.dll");
        File.WriteAllText(dest, "original");
        File.WriteAllText(tempOutput, "obfuscated");
        File.WriteAllText(Path.Combine(tempDir, "App.launcher.exe"), "launcher");
        File.WriteAllText(Path.Combine(tempDir, "App.launcher.runtimeconfig.json"), "{}");
        File.WriteAllText(Path.Combine(tempDir, "App.dll.obfycache"), "cache");

        InPlaceOutputCopy.CopyTempDirectoryToDestination(tempOutput, dest);

        File.ReadAllText(dest).ShouldBe("obfuscated");
        File.ReadAllText(Path.Combine(destDir, "App.launcher.exe")).ShouldBe("launcher");
        File.ReadAllText(Path.Combine(destDir, "App.launcher.runtimeconfig.json")).ShouldBe("{}");
        File.ReadAllText(Path.Combine(destDir, "App.dll.obfycache")).ShouldBe("cache");
        File.Exists(dest + ".obfynew").ShouldBeFalse();
    }

    [Fact]
    public void Copy_MissingTempOutput_ThrowsAndLeavesDestination()
    {
        var dest = Path.Combine(_dir, "App.dll");
        File.WriteAllText(dest, "original");
        var missing = Path.Combine(_dir, "missing", "App.dll");

        Should.Throw<FileNotFoundException>(() =>
            InPlaceOutputCopy.CopyTempDirectoryToDestination(missing, dest));

        File.ReadAllText(dest).ShouldBe("original");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* ignore */ }
        GC.SuppressFinalize(this);
    }
}
