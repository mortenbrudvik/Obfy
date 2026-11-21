using System.Diagnostics;
using System.Text;
using Shouldly;

namespace Obfy.Tests;

/// <summary>
/// The source stamp is compared across checkouts (Windows CRLF working trees, Linux/macOS LF checkouts,
/// git blobs), so it must not depend on how a checkout terminates its lines.
/// </summary>
public class NativeHostSourceStampLineEndingTests
{
    private static readonly string[] Inputs =
    [
        "Src/Obfy.NativeHost/host.c",
        "Src/Obfy.NativeHost/build.ps1",
        "Src/Obfy.PackedBootstrap/PackedBootstrap.cs",
        "Src/Obfy.PackedBootstrap/Obfy.PackedBootstrap.csproj"
    ];

    [Fact]
    public void Stamp_IsTheSameForLfAndCrlfCheckouts()
    {
        using var lf = CopyInputs(LineEnding.Lf);
        using var crlf = CopyInputs(LineEnding.Crlf);

        var lfStamp = Stamp(lf.Path);
        var crlfStamp = Stamp(crlf.Path);

        lfStamp.ShouldBe(crlfStamp);
    }

    [Fact]
    public void Stamp_StillChangesWhenTheContentChanges()
    {
        using var tree = CopyInputs(LineEnding.Crlf);
        var before = Stamp(tree.Path);

        File.AppendAllText(Path.Combine(tree.Path, "Src", "Obfy.NativeHost", "host.c"), "// changed\r\n");

        Stamp(tree.Path).ShouldNotBe(before);
    }

    [Fact]
    public void CheckedInStamp_MatchesAnLfCheckoutOfTheSources()
    {
        using var lf = CopyInputs(LineEnding.Lf);
        var stamp = Path.Combine(RepoRoot(), "Src", "Obfy.NativeHost", "dist", "sources.sha256");

        var (code, _, stderr) = Run(
            "Test-NativeHostSourceStamp.ps1",
            $"-RepoRoot \"{lf.Path}\" -StampPath \"{stamp}\"");

        code.ShouldBe(0, stderr);
    }

    private static string Stamp(string root)
    {
        var (code, stdout, stderr) = Run("Get-NativeHostSourceStamp.ps1", $"-RepoRoot \"{root}\"");
        code.ShouldBe(0, stderr);
        return stdout.Trim();
    }

    private enum LineEnding
    {
        Lf,
        Crlf
    }

    private static TempTree CopyInputs(LineEnding ending)
    {
        var root = Path.Combine(Path.GetTempPath(), "obfy-host-stamp-eol-" + Guid.NewGuid().ToString("N"));
        var source = RepoRoot();
        foreach (var relative in Inputs)
        {
            var from = Path.Combine(source, relative.Replace('/', Path.DirectorySeparatorChar));
            var to = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);

            // Latin-1 maps every byte to one char, so the conversion cannot disturb non-ASCII bytes.
            var text = Encoding.Latin1.GetString(File.ReadAllBytes(from)).Replace("\r\n", "\n");
            if (ending == LineEnding.Crlf)
                text = text.Replace("\n", "\r\n");
            File.WriteAllBytes(to, Encoding.Latin1.GetBytes(text));
        }

        return new TempTree(root);
    }

    private static (int ExitCode, string Stdout, string Stderr) Run(string scriptName, string args)
    {
        var script = Path.Combine(RepoRoot(), "build", scriptName);
        var start = new ProcessStartInfo("pwsh", $"-NoProfile -File \"{script}\" {args}")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        using var process = Process.Start(start);
        process.ShouldNotBeNull();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, stdoutTask.GetAwaiter().GetResult(), stderr);
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
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
