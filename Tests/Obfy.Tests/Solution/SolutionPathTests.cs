using Obfy.Core.Services.Solution;
using Shouldly;

namespace Obfy.Tests.Solution;

public class SolutionPathTests
{
    [Fact]
    public void Normalize_BackslashSegments_UseOsDirectorySeparator()
    {
        SolutionPath.Normalize(@"Lib\Lib.csproj")
            .ShouldBe(Path.Combine("Lib", "Lib.csproj"));
    }

    [Fact]
    public void Resolve_BackslashRelativePath_FindsProjectUnderBaseDirectory()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), $"obfy-sln-path-{Guid.NewGuid():N}");
        var projectDir = Path.Combine(baseDir, "Lib");
        Directory.CreateDirectory(projectDir);
        var expected = Path.Combine(projectDir, "Lib.csproj");
        File.WriteAllText(expected, "<Project />");

        try
        {
            var resolved = SolutionPath.Resolve(baseDir, @"Lib\Lib.csproj");

            resolved.ShouldBe(Path.GetFullPath(expected));
            File.Exists(resolved).ShouldBeTrue();
        }
        finally
        {
            Directory.Delete(baseDir, recursive: true);
        }
    }

    [Fact]
    public void Resolve_ForwardSlashRelativePath_FindsProjectUnderBaseDirectory()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), $"obfy-sln-path-{Guid.NewGuid():N}");
        var projectDir = Path.Combine(baseDir, "Lib");
        Directory.CreateDirectory(projectDir);
        var expected = Path.Combine(projectDir, "Lib.csproj");
        File.WriteAllText(expected, "<Project />");

        try
        {
            var resolved = SolutionPath.Resolve(baseDir, "Lib/Lib.csproj");

            resolved.ShouldBe(Path.GetFullPath(expected));
            File.Exists(resolved).ShouldBeTrue();
        }
        finally
        {
            Directory.Delete(baseDir, recursive: true);
        }
    }
}
