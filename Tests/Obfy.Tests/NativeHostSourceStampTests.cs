using System.Diagnostics;
using Shouldly;

namespace Obfy.Tests;

public class NativeHostSourceStampTests
{
    private static readonly string[] Inputs =
    [
        "Src/Obfy.NativeHost/host.c",
        "Src/Obfy.NativeHost/build.ps1",
        "Src/Obfy.PackedBootstrap/PackedBootstrap.cs",
        "Src/Obfy.PackedBootstrap/Obfy.PackedBootstrap.csproj"
    ];

    [Fact]
    public void Stamp_IsStableLowerHex()
    {
        var root = RepoRoot();
        var first = Stamp(root);
        var second = Stamp(root);
        first.ShouldMatch("^[0-9a-f]{64}$");
        second.ShouldBe(first);
    }

    [Theory]
    [InlineData("Src/Obfy.NativeHost/host.c")]
    [InlineData("Src/Obfy.NativeHost/build.ps1")]
    [InlineData("Src/Obfy.PackedBootstrap/PackedBootstrap.cs")]
    [InlineData("Src/Obfy.PackedBootstrap/Obfy.PackedBootstrap.csproj")]
    public void Stamp_ChangesWhenAnInputChanges(string relative)
    {
        using var tree = CopyInputs();
        var before = Stamp(tree.Path);
        var target = Path.Combine(tree.Path, relative.Replace('/', Path.DirectorySeparatorChar));
        File.AppendAllText(target, "\n");
        Stamp(tree.Path).ShouldNotBe(before);
    }

    [Fact]
    public void Stamp_FailsWhenAnInputIsMissing()
    {
        using var tree = CopyInputs();
        File.Delete(Path.Combine(tree.Path, "Src", "Obfy.NativeHost", "host.c"));
        var (code, _, stderr) = Run("Get-NativeHostSourceStamp.ps1", $"-RepoRoot \"{tree.Path}\"");
        code.ShouldNotBe(0);
        stderr.ShouldContain("missing");
    }

    [Fact]
    public void StampFile_AcceptsAMatchAndRejectsAMismatch()
    {
        using var tree = CopyInputs();
        var stampFile = Path.Combine(tree.Path, "stamp.txt");
        File.WriteAllText(stampFile, Stamp(tree.Path) + "\n");

        var (match, _, matchErr) = Run(
            "Test-NativeHostSourceStamp.ps1",
            $"-RepoRoot \"{tree.Path}\" -StampPath \"{stampFile}\"");
        match.ShouldBe(0, matchErr);

        File.WriteAllText(stampFile, new string('a', 64) + "\n");
        var (mismatch, _, mismatchErr) = Run(
            "Test-NativeHostSourceStamp.ps1",
            $"-RepoRoot \"{tree.Path}\" -StampPath \"{stampFile}\"");
        mismatch.ShouldNotBe(0);
        mismatchErr.ShouldContain("do not match");
    }

    [Fact]
    public void CommittedStamp_RejectsASourceChangeEvenAfterTheWorktreeStampIsRewritten()
    {
        using var tree = CopyInputs();
        var stampFile = Path.Combine(tree.Path, "Src", "Obfy.NativeHost", "dist", "sources.sha256");
        Directory.CreateDirectory(Path.GetDirectoryName(stampFile)!);
        File.WriteAllText(stampFile, Stamp(tree.Path) + "\n");
        Git(tree.Path, "init -b main");
        Git(tree.Path, "add --all");
        Git(tree.Path, "-c user.email=obfy-test@example.com -c user.name=obfy-test commit -m stamp");

        var (match, _, matchErr) = CheckHead(tree.Path);
        match.ShouldBe(0, matchErr);

        File.AppendAllText(Path.Combine(tree.Path, "Src", "Obfy.NativeHost", "host.c"), "\n");
        var (stale, _, staleErr) = CheckHead(tree.Path);
        stale.ShouldNotBe(0);
        staleErr.ShouldContain("do not match");

        File.WriteAllText(stampFile, Stamp(tree.Path) + "\n");
        var (rewritten, _, rewrittenErr) = CheckHead(tree.Path);
        rewritten.ShouldNotBe(0);
        rewrittenErr.ShouldContain("do not match");
    }

    [Fact]
    public void CheckedInStamp_MatchesCurrentSources()
    {
        var stamp = Path.Combine(RepoRoot(), "Src", "Obfy.NativeHost", "dist", "sources.sha256");
        var (code, _, stderr) = Run(
            "Test-NativeHostSourceStamp.ps1",
            $"-RepoRoot \"{RepoRoot()}\" -StampPath \"{stamp}\"");
        code.ShouldBe(0, stderr);
    }

    private static (int ExitCode, string Stdout, string Stderr) CheckHead(string root) =>
        Run("Test-NativeHostSourceStamp.ps1", $"-RepoRoot \"{root}\" -FromGitHead");

    private static string Stamp(string root)
    {
        var (code, stdout, stderr) = Run("Get-NativeHostSourceStamp.ps1", $"-RepoRoot \"{root}\"");
        code.ShouldBe(0, stderr);
        return stdout.Trim();
    }

    private static TempTree CopyInputs()
    {
        var root = Path.Combine(Path.GetTempPath(), "obfy-host-stamp-" + Guid.NewGuid().ToString("N"));
        var source = RepoRoot();
        foreach (var relative in Inputs)
        {
            var from = Path.Combine(source, relative.Replace('/', Path.DirectorySeparatorChar));
            var to = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Copy(from, to);
        }

        return new TempTree(root);
    }

    private static void Git(string root, string args)
    {
        var (code, _, stderr) = RunProcess("git", $"-C \"{root}\" {args}");
        code.ShouldBe(0, stderr);
    }

    private static (int ExitCode, string Stdout, string Stderr) Run(string scriptName, string args)
    {
        var script = Path.Combine(RepoRoot(), "build", scriptName);
        return RunProcess("pwsh", $"-NoProfile -File \"{script}\" {args}");
    }

    private static (int ExitCode, string Stdout, string Stderr) RunProcess(string fileName, string args)
    {
        var start = new ProcessStartInfo(fileName, args)
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

    private sealed class TempTree(string path) : IDisposable
    {
        public string Path { get; } = path;

        public void Dispose()
        {
            if (!Directory.Exists(Path))
                return;

            foreach (var file in Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(Path, recursive: true);
        }
    }
}
