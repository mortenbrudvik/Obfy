using System.Diagnostics;
using Shouldly;

namespace Obfy.Tests;

public class AssertNoSkippedTestsTests
{
    [Fact]
    public void AcceptsExecutedTests()
    {
        using var dir = new TempDir();
        WriteTrx(dir.Path, "passed.trx", total: 3, notExecuted: 0);
        var (code, stdout, stderr) = Run(dir.Path);
        code.ShouldBe(0, stderr);
        stdout.ShouldContain("3");
    }

    [Fact]
    public void RejectsASkippedTest()
    {
        using var dir = new TempDir();
        WriteTrx(dir.Path, "skipped.trx", total: 3, notExecuted: 1);
        var (code, _, stderr) = Run(dir.Path);
        code.ShouldNotBe(0);
        stderr.ShouldContain("were skipped");
    }

    [Fact]
    public void RejectsAFilterThatMatchedNothing()
    {
        using var dir = new TempDir();
        WriteTrx(dir.Path, "empty.trx", total: 0, notExecuted: 0);
        var (code, _, stderr) = Run(dir.Path);
        code.ShouldNotBe(0);
        stderr.ShouldContain("matched no tests");
    }

    [Fact]
    public void IgnoresAnAssemblyThatHadNoMatchesWhenAnotherRan()
    {
        using var dir = new TempDir();
        WriteTrx(dir.Path, "other.trx", total: 0, notExecuted: 0);
        WriteTrx(dir.Path, "platform.trx", total: 1, notExecuted: 0);
        var (code, _, stderr) = Run(dir.Path);
        code.ShouldBe(0, stderr);
    }

    [Fact]
    public void RejectsASkipInAnyAssembly()
    {
        using var dir = new TempDir();
        WriteTrx(dir.Path, "other.trx", total: 0, notExecuted: 0);
        WriteTrx(dir.Path, "platform.trx", total: 2, notExecuted: 2);
        var (code, _, stderr) = Run(dir.Path);
        code.ShouldNotBe(0);
        stderr.ShouldContain("were skipped");
    }

    [Fact]
    public void RejectsAMissingResultsDirectory()
    {
        var missing = Path.Combine(Path.GetTempPath(), "obfy-trx-missing-" + Guid.NewGuid().ToString("N"));
        var (code, _, stderr) = Run(missing);
        code.ShouldNotBe(0);
        stderr.ShouldContain("No TRX");
    }

    [Fact]
    public void PlatformWorkflow_RunsNativeAotOnPullRequests_AndFailsWhenTestsAreSkipped()
    {
        var yaml = File.ReadAllText(Path.Combine(RepoRoot(), ".github", "workflows", "platform.yml"));
        yaml.ShouldContain("pull_request:");
        yaml.ShouldContain("NativeAotTests");
        yaml.ShouldContain("Assert-NoSkippedTests.ps1");
        yaml.ShouldNotContain("continue-on-error");
    }

    private static void WriteTrx(string directory, string name, int total, int notExecuted)
    {
        var executed = Math.Max(0, total - notExecuted);
        var xml = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
              <ResultSummary outcome="Completed">
                <Counters total="{total}" executed="{executed}" passed="{executed}" failed="0" notExecuted="{notExecuted}" />
              </ResultSummary>
            </TestRun>
            """;
        File.WriteAllText(Path.Combine(directory, name), xml);
    }

    private static (int ExitCode, string Stdout, string Stderr) Run(string resultsDirectory)
    {
        var script = Path.Combine(RepoRoot(), "build", "Assert-NoSkippedTests.ps1");
        var start = new ProcessStartInfo(
            "pwsh",
            $"-NoProfile -File \"{script}\" -ResultsDirectory \"{resultsDirectory}\"")
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

    private sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "obfy-trx-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
