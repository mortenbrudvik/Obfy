using Obfy.Core.Services.Solution;
using Shouldly;

namespace Obfy.Tests.Solution;

public class AssemblyOutputLocatorTests
{
    [Fact]
    public void FindAll_PrefersReleaseOverDebug_ForSameTfm()
    {
        var root = CreateTempDir();
        try
        {
            var releaseDll = Path.Combine(root, "bin", "Release", "net8.0", "App.dll");
            var debugDll = Path.Combine(root, "bin", "Debug", "net8.0", "App.dll");
            WriteEmptyFile(releaseDll);
            WriteEmptyFile(debugDll);

            var found = AssemblyOutputLocator.FindAll(root, "App", new[] { "net8.0" });

            found.Count.ShouldBe(1);
            found[0].ShouldBe(releaseDll);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void FindAll_OldStyleReleaseExe_WithoutTfm_IsReturned()
    {
        var root = CreateTempDir();
        try
        {
            var releaseExe = Path.Combine(root, "bin", "Release", "App.exe");
            WriteEmptyFile(releaseExe);

            var found = AssemblyOutputLocator.FindAll(root, "App", Array.Empty<string>());

            found.Count.ShouldBe(1);
            found[0].ShouldBe(releaseExe);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void FindAll_EmptyTree_ReturnsEmptyList()
    {
        var root = CreateTempDir();
        try
        {
            var found = AssemblyOutputLocator.FindAll(root, "App", new[] { "net8.0" });

            found.ShouldBeEmpty();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"obfy-bin-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void WriteEmptyFile(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, Array.Empty<byte>());
    }
}
