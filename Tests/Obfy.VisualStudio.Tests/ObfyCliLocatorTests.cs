using Obfy.VisualStudio.Services;
using Shouldly;

namespace Obfy.VisualStudio.Tests;

public class ObfyCliLocatorTests
{
    [Fact]
    public void Find_ReturnsFirstExistingExe()
    {
        var root = Path.Combine(Path.GetTempPath(), "obfy-cli-" + Guid.NewGuid().ToString("N"));
        var empty = Path.Combine(root, "empty");
        var foundDir = Path.Combine(root, "found");
        Directory.CreateDirectory(empty);
        Directory.CreateDirectory(foundDir);
        var exe = Path.Combine(foundDir, ObfyCliLocator.ExeName);
        File.WriteAllText(exe, "stub");
        try
        {
            ObfyCliLocator.Find([empty, foundDir]).ShouldBe(exe);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Find_MissingDirectories_ReturnsNull()
    {
        var missing = Path.Combine(Path.GetTempPath(), "obfy-cli-missing-" + Guid.NewGuid().ToString("N"));
        ObfyCliLocator.Find([missing, ""]).ShouldBeNull();
    }
}
