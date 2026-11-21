using System.Security.Cryptography;
using Obfy.App.Tests.Common;
using Shouldly;

namespace Obfy.Console.Tests;

/// <summary>
/// <c>--map</c> and <c>--report</c> are written after the run, to a path the user typed. A path that is one of the run's
/// inputs, one of the files the run writes, the other of the two, or the <c>-c</c> file would replace that file with JSON
/// (or HTML), so the CLI refuses the command line first: exit code 1, a red error that names the option and the file, and
/// one problem-matcher line. Nothing is read, written or created before it says so.
/// </summary>
public class CliMapReportGuardTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ObfyMapGuard_{Guid.NewGuid():N}");

    public CliMapReportGuardTests() => Directory.CreateDirectory(Src);

    private string Src => Path.Combine(_root, "src");

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    public enum Mode
    {
        SingleFile,
        SingleFileBeside,
        SeveralFilesClosedSet,
        SeveralFilesClosedSetDefaultFolder,
        Merge,
        MergeBeside,
        Solution,
        SolutionDefaultFolder,
        SolutionPlusExtraAssembly,
        Project,
        OneSourceFile,
        OneSourceFileBeside,
        TwoSourceFiles,
        TwoSourceFilesDefaultFolder,
        AssemblyAndSourceFile,
    }

    /// <summary>One command line, the files it lists, and the files it will write (by the documented rules).</summary>
    private sealed record Scenario(string Args, IReadOnlyList<string> Inputs, IReadOnlyList<string> Outputs);

    public static IEnumerable<object[]> ModesAndOptions() =>
        from mode in Enum.GetValues<Mode>()
        from option in new[] { "--map", "--report" }
        select new object[] { mode, option };

    public static IEnumerable<object[]> ModesOptionsAndSpellings() =>
        from mode in Enum.GetValues<Mode>()
        from option in new[] { "--map", "--report" }
        from spelling in new[] { "exact", "case", "dotdot" }
        select new object[] { mode, option, spelling };

    private Scenario Setup(Mode mode)
    {
        var src = Src;
        var outDir = Path.Combine(_root, "out");
        string Dll(string name) => ConsoleTestAssembly.Create(src, name);
        string Cs(string name)
        {
            var path = Path.Combine(src, name);
            File.WriteAllText(path, $"class {Path.GetFileNameWithoutExtension(name)} {{}}");
            return path;
        }

        switch (mode)
        {
            case Mode.SingleFile:
            {
                var app = Dll("app.dll");
                return new($"\"{app}\" -o \"{outDir}\"", [app], [Path.Combine(outDir, "app.dll")]);
            }
            case Mode.SingleFileBeside:
            {
                var app = Dll("app.dll");
                return new($"\"{app}\"", [app], [Path.Combine(src, "app.obfuscated.dll")]);
            }
            case Mode.SeveralFilesClosedSet:
            {
                var (a, b) = (Dll("A.dll"), Dll("B.dll"));
                return new($"\"{a}\" \"{b}\" -o \"{outDir}\"", [a, b], [Path.Combine(outDir, "A.dll"), Path.Combine(outDir, "B.dll")]);
            }
            case Mode.SeveralFilesClosedSetDefaultFolder:
            {
                var (a, b) = (Dll("A.dll"), Dll("B.dll"));
                var folder = Path.Combine(src, "obfy-out");
                return new($"\"{a}\" \"{b}\"", [a, b], [Path.Combine(folder, "A.dll"), Path.Combine(folder, "B.dll")]);
            }
            case Mode.Merge:
            {
                var (a, b) = (Dll("A.dll"), Dll("B.dll"));
                return new($"\"{a}\" \"{b}\" --merge -o \"{outDir}\"", [a, b], [Path.Combine(outDir, "A.dll")]);
            }
            case Mode.MergeBeside:
            {
                var (a, b) = (Dll("A.dll"), Dll("B.dll"));
                return new($"\"{a}\" \"{b}\" --merge", [a, b], [Path.Combine(src, "A.merged.dll")]);
            }
            case Mode.Solution:
            {
                var (sln, project, built) = WriteLibrarySolution();
                return new($"\"{sln}\" -o \"{outDir}\"", [sln, project, built], [Path.Combine(outDir, "Lib.dll")]);
            }
            case Mode.SolutionDefaultFolder:
            {
                var (sln, project, built) = WriteLibrarySolution();
                return new($"\"{sln}\"", [sln, project, built], [Path.Combine(Path.GetDirectoryName(sln)!, "obfy-out", "Lib.dll")]);
            }
            case Mode.SolutionPlusExtraAssembly:
            {
                var (sln, project, built) = WriteLibrarySolution();
                var extra = Dll("Extra.dll");
                return new(
                    $"\"{sln}\" \"{extra}\" -o \"{outDir}\"",
                    [sln, project, built, extra],
                    [Path.Combine(outDir, "Lib.dll"), Path.Combine(outDir, "Extra.dll")]);
            }
            case Mode.Project:
            {
                var (_, project, built) = WriteLibrarySolution();
                return new($"\"{project}\" -o \"{outDir}\"", [project, built], [Path.Combine(outDir, "Lib.dll")]);
            }
            case Mode.OneSourceFile:
            {
                var x = Cs("X.cs");
                return new($"\"{x}\" -o \"{outDir}\"", [x], [Path.Combine(outDir, "X.cs")]);
            }
            case Mode.OneSourceFileBeside:
            {
                var x = Cs("X.cs");
                return new($"\"{x}\"", [x], [Path.Combine(src, "X.obfuscated.cs")]);
            }
            case Mode.TwoSourceFiles:
            {
                var (x, y) = (Cs("X.cs"), Cs("Y.cs"));
                return new($"\"{x}\" \"{y}\" -o \"{outDir}\"", [x, y], [Path.Combine(outDir, "X.cs"), Path.Combine(outDir, "Y.cs")]);
            }
            case Mode.TwoSourceFilesDefaultFolder:
            {
                var (x, y) = (Cs("X.cs"), Cs("Y.cs"));
                var folder = Path.Combine(src, "obfy-src");
                return new($"\"{x}\" \"{y}\"", [x, y], [Path.Combine(folder, "X.cs"), Path.Combine(folder, "Y.cs")]);
            }
            case Mode.AssemblyAndSourceFile:
            {
                var (app, x) = (Dll("app.dll"), Cs("X.cs"));
                return new($"\"{app}\" \"{x}\" -o \"{outDir}\"", [app, x], [Path.Combine(outDir, "app.dll"), Path.Combine(outDir, "X.cs")]);
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
        }
    }

    private (string Solution, string Project, string Built) WriteLibrarySolution()
    {
        var projectDir = Path.Combine(_root, "src", "Lib");
        var builtDir = Path.Combine(projectDir, "bin", "Release", "net8.0");
        Directory.CreateDirectory(builtDir);
        var project = Path.Combine(projectDir, "Lib.csproj");
        File.WriteAllText(project, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
        var built = ConsoleTestAssembly.Create(builtDir, "Lib.dll");
        var sln = Path.Combine(_root, "src", "Lib.sln");
        File.WriteAllText(sln, """
            Microsoft Visual Studio Solution File, Format Version 12.00
            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Lib", "Lib\Lib.csproj", "{22222222-2222-2222-2222-222222222222}"
            EndProject
            """);
        return (sln, project, built);
    }

    /// <summary>The same file under another spelling: another letter case (Windows), or a <c>..</c> segment.</summary>
    private static string Spell(string path, string spelling) => spelling switch
    {
        "exact" => path,
        "case" => OperatingSystem.IsWindows() ? path.ToUpperInvariant() : path,
        "dotdot" => Path.Combine(Path.GetDirectoryName(path)!, "elsewhere", "..", Path.GetFileName(path)),
        _ => throw new ArgumentOutOfRangeException(nameof(spelling), spelling, null)
    };

    /// <summary>Every file and folder under the test root, with a hash for the files: a refusal must leave all of it as it was.</summary>
    private string Snapshot()
    {
        var lines = new List<string>();
        foreach (var entry in Directory.EnumerateFileSystemEntries(_root, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(_root, entry);
            lines.Add(File.Exists(entry)
                ? $"{relative}|{Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(entry)))}"
                : relative + Path.DirectorySeparatorChar);
        }

        lines.Sort(StringComparer.Ordinal);
        return string.Join('\n', lines);
    }

    private static async Task<(int Exit, string Text, IReadOnlyList<ProblemLine> Problems)> RunAsync(string args)
    {
        using var console = new AnsiConsoleCapture();
        using var capture = new ProblemLineCapture();
        var exit = await CommandLineTestHelpers.InvokeAsync(Program.CreateRootCommand(), args + " -l minimal --no-logo");
        return (exit, console.Text, capture.Problems);
    }

    /// <summary>The run was refused: code 1, the red error and one matcher line name the option and the file, nothing on disk changed.</summary>
    private async Task AssertRefusedAsync(string args, string option, string given, string before)
    {
        var (exit, text, problems) = await RunAsync(args);

        exit.ShouldBe(1, text);
        var full = Path.GetFullPath(given);
        text.ShouldContain(option);
        text.ShouldContain(full);
        var problem = problems.Where(p => p.Message.StartsWith(option, StringComparison.Ordinal)).ShouldHaveSingleItem(text);
        problem.File.ShouldBe(full);
        problem.Severity.ShouldBe("error");
        problem.Message.ShouldContain(full);
        Snapshot().ShouldBe(before, "a refused run must not read-modify, create or replace anything");
    }

    // ---- The option names a file the run reads ----------------------------------------------------------------

    [Theory]
    [MemberData(nameof(ModesOptionsAndSpellings))]
    public async Task OptionNamingAListedInput_IsRefused_AndTheInputIsUntouched(Mode mode, string option, string spelling)
    {
        var scenario = Setup(mode);
        var before = Snapshot();

        foreach (var input in scenario.Inputs)
        {
            var given = Spell(input, spelling);
            await AssertRefusedAsync($"{scenario.Args} {option} \"{given}\"", option, given, before);
            File.Exists(input).ShouldBeTrue();
        }
    }

    // ---- The option names a file the run writes ---------------------------------------------------------------

    [Theory]
    [MemberData(nameof(ModesOptionsAndSpellings))]
    public async Task OptionNamingAPlannedOutput_IsRefused_AndNothingIsWritten(Mode mode, string option, string spelling)
    {
        var scenario = Setup(mode);
        var before = Snapshot();

        foreach (var output in scenario.Outputs)
        {
            var given = Spell(output, spelling);
            await AssertRefusedAsync($"{scenario.Args} {option} \"{given}\"", option, given, before);
            File.Exists(output).ShouldBeFalse("the output the option named must not have been written");
        }
    }

    [Theory]
    [MemberData(nameof(ModesAndOptions))]
    public async Task OptionNamingAPlannedOutput_IsRefusedInADryRunToo(Mode mode, string option)
    {
        var scenario = Setup(mode);
        var before = Snapshot();
        var given = scenario.Outputs[0];

        await AssertRefusedAsync($"{scenario.Args} --dry-run {option} \"{given}\"", option, given, before);
    }

    [Theory]
    [InlineData("--map")]
    [InlineData("--report")]
    public async Task OptionNamingAProjectFileOrBuildOutputOfASolution_SaysTheSolutionListsIt(string option)
    {
        var scenario = Setup(Mode.Solution);
        var before = Snapshot();

        foreach (var listed in scenario.Inputs.Skip(1))
        {
            var (exit, text, problems) = await RunAsync($"{scenario.Args} {option} \"{listed}\"");

            exit.ShouldBe(1, text);
            var message = problems.ShouldHaveSingleItem(text).Message;
            message.ShouldContain("a project file or build output that the solution or project lists");
            message.ShouldNotContain("an input file");
        }

        var (solutionExit, _, solutionProblems) = await RunAsync($"{scenario.Args} {option} \"{scenario.Inputs[0]}\"");
        solutionExit.ShouldBe(1);
        solutionProblems.ShouldHaveSingleItem().Message.ShouldContain("it is an input file");
        Snapshot().ShouldBe(before);
    }

    [Fact]
    public async Task OptionNamingAFileTheRunOnlyMayWrite_SaysSo()
    {
        // The incremental cache file is written with --incremental and removed without it, and a library's packing files
        // are not written at all: "writes" would be wrong for some of these, "may write or remove" is true for all.
        var scenario = Setup(Mode.SingleFile);
        var cache = scenario.Outputs[0] + ".obfycache";

        var (exit, text, problems) = await RunAsync($"{scenario.Args} --map \"{cache}\"");

        exit.ShouldBe(1, text);
        problems.ShouldHaveSingleItem(text).Message.ShouldContain("it is a file the run may write or remove");
    }

    // ---- The option names the configuration file --------------------------------------------------------------

    [Theory]
    [MemberData(nameof(ModesAndOptions))]
    public async Task OptionNamingTheConfigFile_IsRefused_AndTheConfigIsUntouched(Mode mode, string option)
    {
        var scenario = Setup(mode);
        var config = Path.Combine(_root, "obfy.json");
        File.WriteAllText(config, "{}");
        var before = Snapshot();

        await AssertRefusedAsync($"{scenario.Args} -c \"{config}\" {option} \"{config}\"", option, config, before);
        File.ReadAllText(config).ShouldBe("{}");
    }

    // ---- The two options name the same file -------------------------------------------------------------------

    [Theory]
    [InlineData(Mode.SingleFile, "exact")]
    [InlineData(Mode.SingleFile, "case")]
    [InlineData(Mode.SingleFile, "dotdot")]
    [InlineData(Mode.SeveralFilesClosedSet, "exact")]
    [InlineData(Mode.Merge, "case")]
    [InlineData(Mode.Solution, "dotdot")]
    [InlineData(Mode.TwoSourceFiles, "exact")]
    public async Task MapAndReportNamingTheSameFile_IsRefused(Mode mode, string spelling)
    {
        var scenario = Setup(mode);
        var shared = Path.Combine(_root, "reports", "both.json");
        var before = Snapshot();
        var reportSpelling = Spell(shared, spelling);

        var (exit, text, problems) = await RunAsync($"{scenario.Args} --map \"{shared}\" --report \"{reportSpelling}\"");

        exit.ShouldBe(1, text);
        text.ShouldContain("--report");
        text.ShouldContain("--map");
        var problem = problems.ShouldHaveSingleItem(text);
        problem.File.ShouldBe(Path.GetFullPath(reportSpelling));
        problem.Severity.ShouldBe("error");
        problem.Message.ShouldStartWith("--report");
        Snapshot().ShouldBe(before);
    }

    [JunctionFact]
    public async Task OptionNamingTheInputThroughAJunction_IsRefused()
    {
        var app = ConsoleTestAssembly.Create(Src, "app.dll");
        var link = Path.Combine(_root, "link");
        Junctions.TryCreate(link, Src, out var error).ShouldBeTrue(error);
        var outDir = Path.Combine(_root, "out");
        var before = Snapshot();
        var viaLink = Path.Combine(link, "app.dll");

        await AssertRefusedAsync($"\"{app}\" -o \"{outDir}\" --map \"{viaLink}\"", "--map", viaLink, before);
    }

    // ---- Files that belong to a packed output ------------------------------------------------------------------

    [Theory]
    [InlineData("native", "app.runtimeconfig.json")]
    [InlineData("portable", "app.launcher.exe")]
    [InlineData("portable", "app.launcher.runtimeconfig.json")]
    public async Task OptionNamingAFileBesideAPackedOutput_IsRefused(string rid, string sidecarName)
    {
        var app = ConsoleTestAssembly.Create(Src, "app.dll");
        var outDir = Path.Combine(_root, "out");
        var config = Path.Combine(_root, "packing.json");
        File.WriteAllText(config, rid == "native"
            ? """{ "packing": { "enabled": true } }"""
            : """{ "packing": { "enabled": true, "rid": "portable" } }""");
        var before = Snapshot();
        var sidecar = Path.Combine(outDir, sidecarName);

        await AssertRefusedAsync($"\"{app}\" -o \"{outDir}\" -c \"{config}\" --report \"{sidecar}\"", "--report", sidecar, before);
    }

    [Fact]
    public async Task OptionNamingTheIncrementalCacheFileOfAnOutput_IsRefused()
    {
        var app = ConsoleTestAssembly.Create(Src, "app.dll");
        var outDir = Path.Combine(_root, "out");
        var before = Snapshot();
        var cache = Path.Combine(outDir, "app.dll.obfycache");

        await AssertRefusedAsync($"\"{app}\" -o \"{outDir}\" --incremental --map \"{cache}\"", "--map", cache, before);
    }

    // ---- A run with nothing to guard does not plan at all -------------------------------------------------------------

    public static IEnumerable<object[]> Modes() => Enum.GetValues<Mode>().Select(static mode => new object[] { mode });

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task RunWithoutMapOrReport_DoesNotPlanItsOutputs(Mode mode)
    {
        var scenario = Setup(mode);
        var before = RunPathGuard.PlansBuilt;

        var (exit, text, _) = await RunAsync($"{scenario.Args} --dry-run");

        exit.ShouldBe(0, text);
        RunPathGuard.PlansBuilt.ShouldBe(before, "planning is only worth doing when there is a map or a report to compare with it");
    }

    [Theory]
    [MemberData(nameof(ModesAndOptions))]
    public async Task RunWithMapOrReport_PlansItsOutputs(Mode mode, string option)
    {
        var scenario = Setup(mode);
        var elsewhere = Path.Combine(_root, "elsewhere", "result.json");
        var before = RunPathGuard.PlansBuilt;

        var (exit, text, _) = await RunAsync($"{scenario.Args} --dry-run {option} \"{elsewhere}\"");

        exit.ShouldBe(0, text);
        RunPathGuard.PlansBuilt.ShouldBeGreaterThan(before, "the counter is wired: a guarded run does plan");
    }

    // ---- What is still allowed ----------------------------------------------------------------------------------

    [Fact]
    public async Task MapAndReportBesideTheOutput_AreWritten_AndNothingIsRefused()
    {
        var app = ConsoleTestAssembly.Create(Src, "app.dll");
        var outDir = Path.Combine(_root, "out");
        Directory.CreateDirectory(outDir);
        var map = Path.Combine(outDir, "map.json");
        var report = Path.Combine(outDir, "report.json");

        var (exit, text, problems) = await RunAsync($"\"{app}\" -o \"{outDir}\" --rename --map \"{map}\" --report \"{report}\"");

        exit.ShouldBe(0, text);
        File.Exists(Path.Combine(outDir, "app.dll")).ShouldBeTrue();
        File.Exists(map).ShouldBeTrue();
        File.Exists(report).ShouldBeTrue();
        problems.ShouldBeEmpty();
    }

    [Fact]
    public async Task InPlaceOutputFolder_IsNotRefused_WhenMapAndReportGoElsewhere()
    {
        // The documented MSBuild step (obfy $(TargetPath) -o $(TargetDir)) replaces the input in place; only the
        // map and report paths are guarded here.
        var app = ConsoleTestAssembly.Create(Src, "app.dll", typeName: "KeepOrRename");
        var before = File.ReadAllBytes(app);
        var map = Path.Combine(Directory.CreateDirectory(Path.Combine(_root, "maps")).FullName, "map.json");
        var report = Path.Combine(_root, "maps", "report.html");

        var (exit, text, problems) = await RunAsync($"\"{app}\" -o \"{Path.GetDirectoryName(app)}\" --rename --map \"{map}\" --report \"{report}\"");

        exit.ShouldBe(0, text);
        File.ReadAllBytes(app).ShouldNotBe(before, "the output replaces the input in place");
        File.Exists(map).ShouldBeTrue();
        File.Exists(report).ShouldBeTrue();
        problems.ShouldBeEmpty();
    }
}
