using System.Diagnostics;
using Shouldly;

namespace Obfy.Tests;

public class ResolvePackageVersionTests
{
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

    private static (int ExitCode, string Stdout, string Stderr) Run(string refName)
    {
        var script = Path.Combine(RepoRoot(), "build", "Resolve-PackageVersion.ps1");
        var start = new ProcessStartInfo("pwsh", $"-NoProfile -File \"{script}\" -RefName \"{refName}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        using var process = Process.Start(start);
        process.ShouldNotBeNull();
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, stdout, stderr);
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
