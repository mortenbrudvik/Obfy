using Obfy.VisualStudio.Services;
using Shouldly;

namespace Obfy.VisualStudio.Tests;

public class OutputAssemblyLocatorTests
{
    [Fact]
    public void ResolveExisting_PrefersMsBuildOutputPathWhenFileExists()
    {
        var root = Path.Combine(Path.GetTempPath(), "obfy-vs-" + Guid.NewGuid().ToString("N"));
        var outputDir = Path.Combine(root, "bin", "Release");
        Directory.CreateDirectory(outputDir);
        var dll = Path.Combine(outputDir, "App.dll");
        File.WriteAllBytes(dll, [0]);
        try
        {
            var resolved = OutputAssemblyLocator.ResolveExisting(root, "bin\\Release", "App.dll", "App");
            resolved.ShouldBe(dll);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ResolveExisting_FallsBackToBinReleaseTfm()
    {
        var root = Path.Combine(Path.GetTempPath(), "obfy-vs-" + Guid.NewGuid().ToString("N"));
        var fallbackDir = Path.Combine(root, "bin", "Release", "net10.0");
        Directory.CreateDirectory(fallbackDir);
        var dll = Path.Combine(fallbackDir, "Lib.dll");
        File.WriteAllBytes(dll, [0]);
        try
        {
            var resolved = OutputAssemblyLocator.ResolveExisting(root, "missing", "Lib.dll", "Lib");
            resolved.ShouldBe(dll);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ResolveExisting_ReturnsNullWhenNothingExists()
    {
        var root = Path.Combine(Path.GetTempPath(), "obfy-vs-missing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            OutputAssemblyLocator.ResolveExisting(root, "bin\\Release", "Nope.dll", "Nope").ShouldBeNull();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
