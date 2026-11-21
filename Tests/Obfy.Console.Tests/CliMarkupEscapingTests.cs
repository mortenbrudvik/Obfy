using Shouldly;

namespace Obfy.Console.Tests;

/// <summary>
/// A path or file name that contains square brackets (<c>C:\build\App [Release]\bin</c>) must reach the
/// terminal as text. Spectre reads <c>[Release]</c> as a style tag and throws, which used to turn a finished
/// run into exit code 1 after the output was written and skip the map and the report.
/// </summary>
public class CliMarkupEscapingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ObfyMarkup_{Guid.NewGuid():N}");

    public CliMarkupEscapingTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    private string BracketedDir(string name)
    {
        var dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(string args)
    {
        using var capture = new AnsiConsoleCapture();
        var exitCode = await CommandLineTestHelpers.InvokeAsync(Program.CreateRootCommand(), args);
        return (exitCode, capture.Text);
    }

    [Fact]
    public async Task SingleFile_BracketedInputAndOutputPaths_SucceedsAndPrintsPaths()
    {
        var inputDir = BracketedDir("App [Release]");
        var input = ConsoleTestAssembly.Create(inputDir, "Lib [x64].dll");
        var outputDir = Path.Combine(_root, "out [obf]");
        var map = Path.Combine(BracketedDir("maps [1]"), "map [a].json");
        var report = Path.Combine(BracketedDir("reports [2]"), "report [b].json");

        var (exitCode, output) = await RunAsync(
            $"\"{input}\" -o \"{outputDir}\" -l minimal --rename --map \"{map}\" --report \"{report}\" --no-logo");

        output.ShouldNotContain("Could not find color or style");
        exitCode.ShouldBe(0);
        var written = Path.Combine(outputDir, "Lib [x64].dll");
        File.Exists(written).ShouldBeTrue();
        File.Exists(map).ShouldBeTrue("the symbol map is written after the table is shown");
        File.Exists(report).ShouldBeTrue("the report is written after the table is shown");
        output.ShouldContain(written);
        output.ShouldContain($"Symbol map written to {map}");
        output.ShouldContain($"Report written to {report}");
        output.ShouldContain("Obfuscation complete!");
    }

    [Fact]
    public async Task MissingBracketedFile_IsReportedAndTheNextFileStillRuns()
    {
        var existing = ConsoleTestAssembly.Create(BracketedDir("one [x]"), "A [1].dll");
        var missing = Path.Combine(_root, "Missing [Debug].dll");
        var outputDir = Path.Combine(_root, "out");

        // Only one listed file exists, so this is not a closed set: each file is run on its own.
        var (exitCode, output) = await RunAsync(
            $"\"{missing}\" \"{existing}\" -o \"{outputDir}\" -l minimal --no-logo");

        exitCode.ShouldBe(1, "the missing file is an error");
        output.ShouldNotContain("Could not find color or style");
        output.ShouldContain($"File not found: {missing}");
        File.Exists(Path.Combine(outputDir, "A [1].dll")).ShouldBeTrue("a failure on one file must not skip the next");
    }

    [Fact]
    public async Task DryRun_BracketedFileName_PrintsNameWithoutThrowing()
    {
        var input = ConsoleTestAssembly.Create(BracketedDir("dry [run]"), "Tool [Debug].dll");

        var (exitCode, output) = await RunAsync($"\"{input}\" --dry-run --no-logo");

        output.ShouldNotContain("Could not find color or style");
        exitCode.ShouldBe(0);
        output.ShouldContain("Would obfuscate Tool [Debug].dll");
    }

    [Fact]
    public async Task Merge_BracketedPaths_SucceedsAndPrintsNames()
    {
        var dir = BracketedDir("merge [in]");
        var a = ConsoleTestAssembly.Create(dir, "A [m].dll");
        var b = ConsoleTestAssembly.Create(dir, "B [m].dll");
        var outputDir = Path.Combine(_root, "merged [out]");
        var map = Path.Combine(BracketedDir("mm [map]"), "map.json");

        var (exitCode, output) = await RunAsync(
            $"\"{a}\" \"{b}\" --merge -o \"{outputDir}\" -l minimal --rename --map \"{map}\" --no-logo");

        output.ShouldNotContain("Could not find color or style");
        exitCode.ShouldBe(0);
        output.ShouldContain("A [m].dll, B [m].dll");
        output.ShouldContain(Path.Combine(outputDir, "A [m].dll"));
        File.Exists(map).ShouldBeTrue();
    }

    [Fact]
    public async Task ClosedSet_BracketedPaths_SucceedsAndPrintsPaths()
    {
        var (libPath, appPath) = ConsoleTestAssembly.CreateClosedSetPair(BracketedDir("set [1]"));
        var outputDir = Path.Combine(_root, "set-out [x]");
        var report = Path.Combine(BracketedDir("rep [r]"), "report.json");

        var (exitCode, output) = await RunAsync(
            $"\"{appPath}\" \"{libPath}\" -o \"{outputDir}\" -l minimal --report \"{report}\" --no-logo");

        output.ShouldNotContain("Could not find color or style");
        exitCode.ShouldBe(0);
        File.Exists(Path.Combine(outputDir, "Lib.dll")).ShouldBeTrue();
        File.Exists(report).ShouldBeTrue();
        output.ShouldContain($"Report written to {report}");
    }

    [Fact]
    public async Task ConfigGenerate_BracketedPath_PrintsPath()
    {
        var path = Path.Combine(BracketedDir("cfg [dir]"), "obfy [1].json");

        var (exitCode, output) = await RunAsync($"config generate -o \"{path}\"");

        output.ShouldNotContain("Could not find color or style");
        exitCode.ShouldBe(0);
        File.Exists(path).ShouldBeTrue();
        output.ShouldContain($"Configuration file created: {path}");
    }

    [Fact]
    public async Task Failure_BracketedNameAndMessage_PrintsErrorWithoutThrowing()
    {
        // Not an assembly: the load fails and the engine's message may quote the path.
        var dir = BracketedDir("bad [x]");
        var bad = Path.Combine(dir, "Broken [1].dll");
        File.WriteAllText(bad, "this is not a PE file [oops]");

        var (exitCode, output) = await RunAsync($"\"{bad}\" -o \"{Path.Combine(_root, "o")}\" --no-logo");

        output.ShouldNotContain("Could not find color or style");
        exitCode.ShouldBe(1);
        output.ShouldContain("Failed to obfuscate Broken [1].dll");
    }
}
