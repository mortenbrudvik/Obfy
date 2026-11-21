using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Obfy.App.Tests.Common;
using Shouldly;

namespace Obfy.Console.Tests;

/// <summary>
/// <c>--incremental</c> through the real command line. A second run of an unchanged input reuses the output of the
/// first, and must still show the first run's numbers. The symbol map and the report need what only a real run has
/// (the cache never stores original names), so a run that asks for either is done again instead of writing an empty
/// one.
/// </summary>
public class CliIncrementalCacheTests : IDisposable
{
    private const string Reused = "Incremental: reused cached output";
    private const string NotUsedForMap = "Incremental cache not used for this run: the symbol map is needed";

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ObfyIncremental_{Guid.NewGuid():N}");

    public CliIncrementalCacheTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(string args)
    {
        using var capture = new AnsiConsoleCapture();
        var exitCode = await CommandLineTestHelpers.InvokeAsync(Program.CreateRootCommand(), args);
        return (exitCode, capture.Text);
    }

    private static int TotalTransformations(string output)
    {
        var match = Regex.Match(output, @"Total Transformations\D+(\d+)");
        match.Success.ShouldBeTrue("the result table has no Total Transformations row:\n" + output);
        return int.Parse(match.Groups[1].Value);
    }

    private static Dictionary<string, string> ReadMap(string path) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))!;

    private string NewInput(string typeName = "LedgerKeeperUnderTest", string name = "App.dll") =>
        ConsoleTestAssembly.Create(_root, name, typeName);

    private string Out => Path.Combine(_root, "out");

    [Fact]
    public async Task SecondRun_PrintsTheFirstRunsNumbers_NotZeros()
    {
        var input = NewInput();
        var args = $"\"{input}\" -o \"{Out}\" -l minimal --rename --incremental --no-logo";

        var (firstExit, first) = await RunAsync(args);
        var (secondExit, second) = await RunAsync(args);

        firstExit.ShouldBe(0, first);
        secondExit.ShouldBe(0, second);
        first.ShouldNotContain(Reused);
        second.ShouldContain(Reused);
        TotalTransformations(first).ShouldBeGreaterThan(0);
        TotalTransformations(second).ShouldBe(TotalTransformations(first));
        second.ShouldContain("Types Renamed");
    }

    [Fact]
    public async Task SecondRun_WithMap_RunsAgainAndWritesTheSameNonEmptyMap()
    {
        var input = NewInput();
        var map1 = Path.Combine(_root, "map1.json");
        var map2 = Path.Combine(_root, "map2.json");
        var common = $"\"{input}\" -o \"{Out}\" -l minimal --rename --incremental --no-logo";

        var (_, first) = await RunAsync($"{common} --map \"{map1}\"");
        var (secondExit, second) = await RunAsync($"{common} --map \"{map2}\"");

        secondExit.ShouldBe(0, second);
        first.ShouldContain($"Symbol map written to {map1}");
        File.Exists(map2).ShouldBeTrue("the second run was asked for a map and must write one");
        second.ShouldContain($"Symbol map written to {map2}");
        second.ShouldNotContain(Reused);
        second.ShouldContain(NotUsedForMap);
        var written = ReadMap(map2);
        written.ShouldNotBeEmpty();
        written.Keys.ShouldBe(ReadMap(map1).Keys, ignoreOrder: true);
        written.Keys.ShouldContain(k => k.Contains("LedgerKeeperUnderTest", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunWithoutMap_AfterARunWithOne_StillReusesTheOutput()
    {
        var input = NewInput();
        var common = $"\"{input}\" -o \"{Out}\" -l minimal --rename --incremental --no-logo";
        await RunAsync($"{common} --map \"{Path.Combine(_root, "map1.json")}\"");
        await RunAsync($"{common} --map \"{Path.Combine(_root, "map2.json")}\"");

        var (exit, output) = await RunAsync(common);

        exit.ShouldBe(0, output);
        output.ShouldContain(Reused);
        output.ShouldNotContain(NotUsedForMap);
        TotalTransformations(output).ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task SecondRun_WithReport_ListsTheRenamedSymbols_NotAnEmptySummary()
    {
        var input = NewInput();
        var report1 = Path.Combine(_root, "report1.json");
        var report2 = Path.Combine(_root, "report2.json");
        var common = $"\"{input}\" -o \"{Out}\" -l minimal --rename --incremental --no-logo";

        await RunAsync($"{common} --report \"{report1}\"");
        var (exit, second) = await RunAsync($"{common} --report \"{report2}\"");

        exit.ShouldBe(0, second);
        second.ShouldNotContain(Reused);
        using var firstDoc = JsonDocument.Parse(File.ReadAllText(report1));
        using var secondDoc = JsonDocument.Parse(File.ReadAllText(report2));
        var firstTotal = firstDoc.RootElement.GetProperty("symbolSummary").GetProperty("totalSymbolsRenamed").GetInt32();
        firstTotal.ShouldBeGreaterThan(0);
        secondDoc.RootElement.GetProperty("symbolSummary").GetProperty("totalSymbolsRenamed").GetInt32().ShouldBe(firstTotal);
    }

    [Fact]
    public async Task Merge_SecondRun_WithMap_RunsAgainAndWritesTheMap()
    {
        var a = NewInput("AlphaLedgerUnderTest", "A.dll");
        var b = NewInput("BetaLedgerUnderTest", "B.dll");
        var map1 = Path.Combine(_root, "map1.json");
        var map2 = Path.Combine(_root, "map2.json");
        var common = $"\"{a}\" \"{b}\" --merge -o \"{Out}\" -l minimal --rename --incremental --no-logo";

        var (firstExit, first) = await RunAsync($"{common} --map \"{map1}\"");
        var (secondExit, second) = await RunAsync($"{common} --map \"{map2}\"");

        firstExit.ShouldBe(0, first);
        secondExit.ShouldBe(0, second);
        second.ShouldNotContain(Reused);
        File.Exists(map2).ShouldBeTrue();
        ReadMap(map2).Keys.ShouldBe(ReadMap(map1).Keys, ignoreOrder: true);
    }

    [Fact]
    public async Task OutputReplacedByTheUnprotectedOriginal_IsProtectedAgain_NotReportedAsReused()
    {
        var input = NewInput();
        var original = File.ReadAllBytes(input);
        var args = $"\"{input}\" -o \"{Out}\" -l minimal --rename --incremental --no-logo";
        var (_, first) = await RunAsync(args);
        var output = Path.Combine(Out, "App.dll");

        // A build copies the unprotected original over the protected output.
        File.WriteAllBytes(output, original);
        var (exit, second) = await RunAsync(args);

        exit.ShouldBe(0, second);
        second.ShouldNotContain(Reused);
        TotalTransformations(second).ShouldBe(TotalTransformations(first));
        File.ReadAllBytes(output).ShouldNotBe(original, "the second run protected the file again");
    }

    [JunctionFact]
    public async Task OutputFolderThatIsAJunctionToTheInputsFolder_IsInPlace_NotAStaleHit()
    {
        var input = NewInput();
        var original = File.ReadAllBytes(input);
        var real = Path.Combine(_root, "real");
        Directory.CreateDirectory(real);
        var link = Path.Combine(_root, "link");
        Junctions.TryCreate(link, real, out var error).ShouldBeTrue(error);
        var common = "-l minimal --rename --incremental --no-logo";
        var (firstExit, firstOutput) = await RunAsync($"\"{input}\" -o \"{real}\" {common}");
        firstExit.ShouldBe(0, firstOutput);
        var target = Path.Combine(real, "App.dll");
        File.WriteAllBytes(target, original); // the build restores the original

        // obfy real\App.dll -o link: the output folder is the input's folder under another name
        var (exit, output) = await RunAsync($"\"{target}\" -o \"{link}\" {common}");

        exit.ShouldBe(0, output);
        output.ShouldNotContain(Reused);
        output.ShouldContain("Incremental cache not used: the output replaces the input file.");
        File.ReadAllBytes(target).ShouldNotBe(original, "the in-place run protected the file");
    }

    [Fact]
    public async Task Report_SecondRun_RunsAgain_WhenTheCachedRunRaisedWarnings_AndReusesWithoutAReport()
    {
        // Virtualization on a method it cannot encode raises a warning and leaves the symbol map empty, so only the
        // report (which lists warnings) must make the second run do the work again.
        var input = NewInput();
        var report = Path.Combine(_root, "report.json");
        var common = $"\"{input}\" -o \"{Out}\" -l minimal --no-symbol-renaming --encrypt-methods --incremental --no-logo";
        var (firstExit, first) = await RunAsync(common);
        firstExit.ShouldBe(0, first);
        using (var entry = JsonDocument.Parse(File.ReadAllText(Path.Combine(Out, "App.dll.obfycache"))))
        {
            entry.RootElement.GetProperty("symbolMapEntries").GetInt32().ShouldBe(0);
            entry.RootElement.GetProperty("warnings").GetInt32().ShouldBeGreaterThan(0, "the sample must raise a warning:\n" + first);
        }

        var (withReportExit, withReport) = await RunAsync($"{common} --report \"{report}\"");
        var (plainExit, plain) = await RunAsync(common);

        withReportExit.ShouldBe(0, withReport);
        withReport.ShouldNotContain(Reused);
        withReport.ShouldContain("Incremental cache not used for this run: the report is needed");
        File.Exists(report).ShouldBeTrue();
        plainExit.ShouldBe(0, plain);
        plain.ShouldContain(Reused);
        plain.ShouldContain("The run that produced this output raised");
    }

    [Fact]
    public async Task CacheFile_NextToTheOutput_HoldsNoOriginalName()
    {
        var input = NewInput();
        var map = Path.Combine(_root, "map1.json");

        var (exit, output) = await RunAsync($"\"{input}\" -o \"{Out}\" -l minimal --rename --incremental --map \"{map}\" --no-logo");

        exit.ShouldBe(0, output);
        var cache = Path.Combine(Out, "App.dll.obfycache");
        File.Exists(cache).ShouldBeTrue();
        var bytes = File.ReadAllBytes(cache);
        foreach (var name in new[] { "LedgerKeeperUnderTest", "TestNamespace", "TestMethod", "Hello, World!" })
        {
            bytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes(name)).ShouldBe(-1, $"'{name}' (UTF-8) is in the cache file");
            bytes.AsSpan().IndexOf(Encoding.Unicode.GetBytes(name)).ShouldBe(-1, $"'{name}' (UTF-16) is in the cache file");
        }

        File.ReadAllText(map).ShouldContain("LedgerKeeperUnderTest", Case.Sensitive, "the map the user asked for is where the names belong");
    }
}
