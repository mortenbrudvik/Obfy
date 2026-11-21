using System.Diagnostics;
using Shouldly;

namespace Obfy.Tests;

public class ResolvePackageVersionTests : IDisposable
{
    private readonly string _scratch = Path.Combine(Path.GetTempPath(), $"ObfyResolveVersion_{Guid.NewGuid():N}");

    public ResolvePackageVersionTests() => Directory.CreateDirectory(_scratch);

    public void Dispose()
    {
        try { Directory.Delete(_scratch, recursive: true); } catch { /* ignore */ }
    }

    [Theory]
    [InlineData("v2.1.0", "2.1.0")]
    [InlineData("2.1.0", "2.1.0")]
    [InlineData("v2.1.0-beta.1", "2.1.0-beta.1")]
    [InlineData("2.1.0+sha", "2.1.0+sha")]
    public void WithVersionFile_AcceptsATagWhoseNumbersMatchVersionJson(string refName, string expected)
    {
        var versionFile = WriteVersionFile("{ \"major\": 2, \"minor\": 1, \"patch\": 0 }");

        var (exitCode, stdout, stderr) = Run(refName, versionFile);

        exitCode.ShouldBe(0, stderr);
        stdout.Trim().ShouldBe(expected);
    }

    [Theory]
    [InlineData("v2.1.1")]
    [InlineData("v3.1.0")]
    [InlineData("2.0.0")]
    [InlineData("v2.1.1-beta.1")]
    public void WithVersionFile_RejectsATagThatDisagreesWithVersionJson(string refName)
    {
        var versionFile = WriteVersionFile("{ \"major\": 2, \"minor\": 1, \"patch\": 0 }");

        var (exitCode, stdout, stderr) = Run(refName, versionFile);

        exitCode.ShouldNotBe(0);
        stdout.Trim().ShouldBeEmpty();
        stderr.ShouldContain("version.json");
        stderr.ShouldContain("2.1.0");
    }

    [Fact]
    public void WithVersionFile_StillRejectsANonVersionTag()
    {
        var versionFile = WriteVersionFile("{ \"major\": 2, \"minor\": 1, \"patch\": 0 }");

        var (exitCode, _, stderr) = Run("release", versionFile);

        exitCode.ShouldNotBe(0);
        stderr.ShouldContain("1.2.3");
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{ \"major\": 2 }")]
    public void WithVersionFile_FailsWhenVersionJsonIsUnusable(string json)
    {
        var (exitCode, stdout, stderr) = Run("v2.1.0", WriteVersionFile(json));

        exitCode.ShouldNotBe(0);
        stdout.Trim().ShouldBeEmpty();
        stderr.ShouldContain("version.json");
    }

    [Fact]
    public void WithVersionFile_FailsWhenTheFileIsMissing()
    {
        var (exitCode, stdout, stderr) = Run("v2.1.0", Path.Combine(_scratch, "missing", "version.json"));

        exitCode.ShouldNotBe(0);
        stdout.Trim().ShouldBeEmpty();
        stderr.ShouldContain("version.json");
    }

    [Theory]
    [InlineData("v1.3.1", "1.3.1")]
    [InlineData("1.3.1", "1.3.1")]
    [InlineData("v1.3.1-beta.1", "1.3.1-beta.1")]
    [InlineData("1.3.1+sha", "1.3.1+sha")]
    public void AcceptsReleaseTags(string refName, string expected)
    {
        var (exitCode, stdout, stderr) = Run(refName);
        exitCode.ShouldBe(0, stderr);
        stdout.Trim().ShouldBe(expected);
    }

    [Theory]
    [InlineData("release")]
    [InlineData("v1.2")]
    [InlineData("1.2.3.4")]
    public void RejectsNonVersions(string refName)
    {
        var (exitCode, _, stderr) = Run(refName);
        exitCode.ShouldNotBe(0);
        stderr.ShouldContain("1.2.3");
    }

    private string WriteVersionFile(string json)
    {
        var path = Path.Combine(_scratch, Guid.NewGuid().ToString("N"), "version.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
        return path;
    }

    private static (int ExitCode, string Stdout, string Stderr) Run(string refName, string? versionFile = null)
    {
        var script = Path.Combine(RepoRoot(), "build", "Resolve-PackageVersion.ps1");
        var start = new ProcessStartInfo("pwsh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in new[] { "-NoProfile", "-File", script, "-RefName", refName })
            start.ArgumentList.Add(argument);
        if (versionFile is not null)
        {
            start.ArgumentList.Add("-VersionFile");
            start.ArgumentList.Add(versionFile);
        }

        using var process = Process.Start(start);
        process.ShouldNotBeNull();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return (process.ExitCode, stdout.Result, stderr.Result);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Obfy.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not find the repository root.");
    }
}
