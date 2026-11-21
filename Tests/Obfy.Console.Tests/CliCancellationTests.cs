using System.CommandLine;
using Shouldly;

namespace Obfy.Console.Tests;

/// <summary>
/// Ctrl+C reaches the engine as the cancellation token System.CommandLine hands to the root action. These tests
/// pass an already-cancelled token through <c>ParseResult.InvokeAsync</c>, the same seam, and check that every
/// input mode honours it: the run reports failure, writes nothing and leaves no temp file behind. The source and
/// closed-set modes always passed the token; the single-file and merge modes did not.
/// </summary>
public class CliCancellationTests : IDisposable
{
    /// <summary>
    /// Program.cs has no handler of its own for a cancelled run. The OperationCanceledException leaves the root action
    /// and System.CommandLine's default exception handler turns it into exit code 1 without printing anything.
    /// </summary>
    private const int CancelledExitCode = 1;

    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), $"ObfyCancel_{Guid.NewGuid():N}");

    public CliCancellationTests() => Directory.CreateDirectory(_tempDirectory);

    public void Dispose()
    {
        try { Directory.Delete(_tempDirectory, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public async Task CancelledToken_SingleFile_StopsWithoutWritingOutput()
    {
        var input = ConsoleTestAssembly.Create(_tempDirectory, "A.dll", "Alpha");
        var outputDir = Path.Combine(_tempDirectory, "single-out");
        Directory.CreateDirectory(outputDir);

        var exitCode = await InvokeCancelledAsync($"\"{input}\" -o \"{outputDir}\" -l minimal --no-logo");

        exitCode.ShouldBe(CancelledExitCode);
        Directory.GetFileSystemEntries(outputDir).ShouldBeEmpty();
    }

    [Fact]
    public async Task CancelledToken_Merge_StopsWithoutWritingOutput()
    {
        var a = ConsoleTestAssembly.Create(_tempDirectory, "A.dll", "Alpha");
        var b = ConsoleTestAssembly.Create(_tempDirectory, "B.dll", "Beta");
        var outputDir = Path.Combine(_tempDirectory, "merge-out");
        Directory.CreateDirectory(outputDir);

        var exitCode = await InvokeCancelledAsync($"\"{a}\" \"{b}\" --merge -o \"{outputDir}\" -l minimal --no-logo");

        exitCode.ShouldBe(CancelledExitCode);
        Directory.GetFileSystemEntries(outputDir).ShouldBeEmpty();
    }

    [Fact]
    public async Task CancelledToken_ClosedSet_StopsWithoutWritingOutput()
    {
        var a = ConsoleTestAssembly.Create(_tempDirectory, "A.dll", "Alpha");
        var b = ConsoleTestAssembly.Create(_tempDirectory, "B.dll", "Beta");
        var outputDir = Path.Combine(_tempDirectory, "closed-set-out");
        Directory.CreateDirectory(outputDir);

        var exitCode = await InvokeCancelledAsync($"\"{a}\" \"{b}\" -o \"{outputDir}\" -l minimal --no-logo");

        exitCode.ShouldBe(CancelledExitCode);
        Directory.GetFileSystemEntries(outputDir).ShouldBeEmpty();
    }

    [Fact]
    public async Task CancelledToken_SourceFiles_StopsWithoutWritingOutput()
    {
        var a = Path.Combine(_tempDirectory, "A.cs");
        var b = Path.Combine(_tempDirectory, "B.cs");
        File.WriteAllText(a, "class A {}");
        File.WriteAllText(b, "class B {}");
        var outputDir = Path.Combine(_tempDirectory, "source-out");
        Directory.CreateDirectory(outputDir);

        var exitCode = await InvokeCancelledAsync($"\"{a}\" \"{b}\" -o \"{outputDir}\" -l minimal --no-logo");

        exitCode.ShouldBe(CancelledExitCode);
        Directory.GetFileSystemEntries(outputDir).ShouldBeEmpty();
    }

    private static async Task<int> InvokeCancelledAsync(string args)
    {
        using var capture = new AnsiConsoleCapture();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var writer = new StringWriter();
        var configuration = new InvocationConfiguration { Output = writer, Error = writer };
        return await Program.CreateRootCommand().Parse(args).InvokeAsync(configuration, cts.Token);
    }
}
