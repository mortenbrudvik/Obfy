using Obfy.Core.Models;
using Obfy.Core.Utilities;
using Shouldly;

namespace Obfy.Console.Tests;

/// <summary>
/// The guard's own rules, without a run: what it compares, in what order, and the exact sentences. The command-line
/// behaviour (every mode, every kind of collision) is in <see cref="CliMapReportGuardTests"/>.
/// </summary>
public class RunPathGuardTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ObfyPathGuard_{Guid.NewGuid():N}");

    public RunPathGuardTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    private string P(params string[] parts) => Path.Combine([_root, .. parts]);

    [Fact]
    public void Message_NamesTheOptionTheFileWhyAndTheWayOut()
    {
        var input = P("app.dll");
        var map = new FileInfo(input);

        var collision = RunPathGuard
            .Find(map, null, [new ProtectedPaths(PathRole.Input, [input])])
            .ShouldHaveSingleItem();

        collision.Option.ShouldBe("--map");
        collision.File.ShouldBe(map.FullName);
        collision.Message.ShouldBe(
            $"--map cannot be '{map.FullName}': it is an input file and the run would replace it with the symbol map. Choose another path for --map.");
        collision.Message.ShouldNotContain("\n");
    }

    [Fact]
    public void Messages_SayWhatTheFileIsToTheRun()
    {
        var file = P("x.json");
        var report = new FileInfo(file);

        string Say(PathRole role) => RunPathGuard.Find(null, report, [new ProtectedPaths(role, [file])]).ShouldHaveSingleItem().Message;

        Say(PathRole.Input).ShouldBe(
            $"--report cannot be '{report.FullName}': it is an input file and the run would replace it with the report. Choose another path for --report.");
        Say(PathRole.SessionFile).ShouldBe(
            $"--report cannot be '{report.FullName}': it is a project file or build output that the solution or project lists, and the run would replace it with the report. Choose another path for --report.");
        Say(PathRole.Config).ShouldBe(
            $"--report cannot be '{report.FullName}': it is the configuration file given with -c and the run would replace it with the report. Choose another path for --report.");
        Say(PathRole.Output).ShouldBe(
            $"--report cannot be '{report.FullName}': it is a file the run may write or remove, and the report would replace it. Choose another path for --report.");
    }

    [Fact]
    public void BothOptionsCollidingWithDifferentFiles_GiveOneCollisionEach()
    {
        var found = RunPathGuard.Find(
            new FileInfo(P("a.dll")),
            new FileInfo(P("b.dll")),
            [new ProtectedPaths(PathRole.Input, [P("a.dll"), P("b.dll")])]);

        found.Select(static c => c.Option).ShouldBe(["--map", "--report"]);
    }

    [Fact]
    public void FirstListedGroupWins_WhenAFileIsInSeveralGroups()
    {
        var file = P("app.dll");

        var found = RunPathGuard.Find(
            new FileInfo(file), null,
            [new ProtectedPaths(PathRole.Input, [file]), new ProtectedPaths(PathRole.Output, [file])]);

        found.ShouldHaveSingleItem().Message.ShouldContain("input file");
    }

    [Fact]
    public void ReportEqualToTheMap_IsRefusedOnlyWhenAsked_AndNamesTheReport()
    {
        var map = new FileInfo(P("both.json"));
        var report = new FileInfo(P("sub", "..", "both.json"));

        RunPathGuard.Find(map, report, []).ShouldBeEmpty("compared only when the caller asks for it");
        var found = RunPathGuard.Find(map, report, [], compareMapWithReport: true).ShouldHaveSingleItem();

        found.Option.ShouldBe("--report");
        found.File.ShouldBe(report.FullName);
        found.Message.ShouldBe(
            $"--report cannot be '{report.FullName}': --map writes the same file and the report would replace the symbol map. Choose another path for --report.");
    }

    [Fact]
    public void ReportEqualToTheMap_IsNotReportedTwice_WhenItAlreadyCollidesWithAnInput()
    {
        var file = P("app.dll");

        var found = RunPathGuard.Find(
            new FileInfo(file), new FileInfo(file),
            [new ProtectedPaths(PathRole.Input, [file])], compareMapWithReport: true);

        found.Select(static c => c.Option).ShouldBe(["--map", "--report"]);
        found.ShouldAllBe(static c => c.Message.Contains("input file", StringComparison.Ordinal));
    }

    [Fact]
    public void WithNeitherOption_TheGroupsAreNeverBuilt()
    {
        var built = false;

        RunPathGuard.Find(null, null, () => { built = true; return []; }, compareMapWithReport: true).ShouldBeEmpty();
        RunPathGuard.Find(new FileInfo(P("map.json")), null, () => { built = true; return []; }).ShouldBeEmpty();

        built.ShouldBeTrue("with an option the groups are asked for");
        var builtForNothing = false;
        RunPathGuard.Find(null, null, () => { builtForNothing = true; return []; });
        builtForNothing.ShouldBeFalse();
    }

    [Fact]
    public void EveryPath_IsResolvedOnce_NotOncePerComparison()
    {
        // A solution session lists hundreds of files and resolving a path walks the file system: the option's own path
        // must not be resolved again for each of them.
        var listed = Enumerable.Range(0, 40).Select(i => P("src", $"Project{i}", "bin", "Lib.dll")).ToList();
        var resolved = new List<string>();
        string Resolve(string path)
        {
            resolved.Add(path);
            return PathIdentity.Resolve(path);
        }

        var found = RunPathGuard.Find(
            new FileInfo(P("map.json")), new FileInfo(P("report.json")),
            () => [new ProtectedPaths(PathRole.Input, listed), new ProtectedPaths(PathRole.Output, [P("out", "Lib.dll")])],
            compareMapWithReport: true, resolve: Resolve);

        found.ShouldBeEmpty();
        resolved.GroupBy(static path => path).Where(static g => g.Count() > 1).Select(static g => g.Key).ShouldBeEmpty();
        resolved.Count.ShouldBe(listed.Count + 1 + 2, "each listed file once, and the map and the report once");
    }

    [Fact]
    public void TheOptionsPath_IsResolvedOnce_EvenWhenTheCollisionIsTheLastCandidate()
    {
        var listed = Enumerable.Range(0, 10).Select(i => P($"f{i}.dll")).ToList();
        var resolved = new List<string>();
        string Resolve(string path)
        {
            resolved.Add(path);
            return PathIdentity.Resolve(path);
        }

        var found = RunPathGuard.Find(
            new FileInfo(listed[^1]), null, () => [new ProtectedPaths(PathRole.Input, listed)], resolve: Resolve);

        found.ShouldHaveSingleItem();
        resolved.Count(path => path == Path.GetFullPath(listed[^1])).ShouldBe(2, "once as the option, once as the candidate it equals");
        resolved.Count.ShouldBe(listed.Count + 1);
    }

    [Fact]
    public void NothingGiven_NothingFound_AndBlankPathsAreIgnored()
    {
        RunPathGuard.Find(null, null, [new ProtectedPaths(PathRole.Input, [P("app.dll")])], compareMapWithReport: true).ShouldBeEmpty();
        RunPathGuard.Find(
            new FileInfo(P("map.json")), null,
            [new ProtectedPaths(PathRole.Config, [null, "", "  "]), new ProtectedPaths(PathRole.Input, [P("other.dll")])])
            .ShouldBeEmpty();
    }

    [Fact]
    public void ADifferentSpellingOfTheSameFile_IsTheSameFile()
    {
        var input = P("src", "App.dll");

        RunPathGuard.Find(new FileInfo(P("src", "x", "..", "App.dll")), null, [new ProtectedPaths(PathRole.Input, [input])])
            .ShouldHaveSingleItem();
        if (OperatingSystem.IsWindows())
        {
            RunPathGuard.Find(new FileInfo(input.ToUpperInvariant()), null, [new ProtectedPaths(PathRole.Input, [input])])
                .ShouldHaveSingleItem();
        }
    }

    [Fact]
    public void WithCompanions_ListsThePackingFilesAndTheCacheFile_OnlyWhenTheyExist()
    {
        var output = P("out", "app.exe");
        var plain = new ObfySettings();
        var native = new ObfySettings { Packing = { Enabled = true } };
        var portable = new ObfySettings { Packing = { Enabled = true, Rid = "portable" } };

        RunPathGuard.WithCompanions(output, plain, cacheFile: false).ShouldBe([output]);
        RunPathGuard.WithCompanions(output, plain, cacheFile: true).ShouldBe([output, output + ".obfycache"]);
        RunPathGuard.WithCompanions(output, native, cacheFile: false).ShouldBe([output, P("out", "app.runtimeconfig.json")]);
        RunPathGuard.WithCompanions(output, portable, cacheFile: false)
            .ShouldBe([output, P("out", "app.launcher.exe"), P("out", "app.launcher.runtimeconfig.json")]);
    }

    // ---- After the run: the paths the run really wrote ----------------------------------------------------------

    [Fact]
    public void FindForResults_RefusesAFileTheRunWroteThatThePlanDidNotKnow()
    {
        var written = P("out", "app.dll");
        var launcher = P("out", "app.launcher.exe");
        var results = new[]
        {
            ObfuscationResult.Successful(new ObfuscationStatistics(), outputPath: written, packedLauncherPath: launcher),
            ObfuscationResult.Failed("no output")
        };

        var map = RunPathGuard.FindForResults(new FileInfo(P("out", "x", "..", "app.dll")), null, results);
        var report = RunPathGuard.FindForResults(null, new FileInfo(launcher), results);
        var clear = RunPathGuard.FindForResults(new FileInfo(P("map.json")), new FileInfo(P("report.json")), results);

        map.ShouldHaveSingleItem().Option.ShouldBe("--map");
        var collision = report.ShouldHaveSingleItem();
        collision.Option.ShouldBe("--report");
        collision.Message.ShouldContain(Path.GetFullPath(launcher));
        collision.Message.ShouldContain("a file the run may write or remove");
        clear.ShouldBeEmpty();
    }

    [Fact]
    public void FindForResults_WithNeitherOption_ReadsNothing()
    {
        RunPathGuard.FindForResults(null, null, null!).ShouldBeEmpty();
    }

    // ---- The plan the CLI builds for a run of separate files ---------------------------------------------------------

    [Fact]
    public void PlanStandardOutputs_FollowsTheRulesTheRunWritesBy()
    {
        var src = Directory.CreateDirectory(P("src")).FullName;
        var dll = ConsoleTestAssembly.Create(src, "app.dll");
        var x = Path.Combine(src, "X.cs");
        var y = Path.Combine(src, "Y.cs");
        File.WriteAllText(x, "class X {}");
        File.WriteAllText(y, "class Y {}");
        var missing = Path.Combine(src, "gone.dll");
        var settings = new ObfySettings();
        var folder = new DirectoryInfo(P("out"));

        var beside = Program.PlanStandardOutputs([new FileInfo(dll), new FileInfo(x), new FileInfo(missing)], null, settings);
        var together = Program.PlanStandardOutputs([new FileInfo(dll), new FileInfo(x), new FileInfo(y)], null, settings);
        var intoFolder = Program.PlanStandardOutputs([new FileInfo(dll), new FileInfo(x), new FileInfo(y)], folder, settings);

        beside.ShouldBe(
        [
            Path.Combine(src, "app.obfuscated.dll"), Path.Combine(src, "app.obfuscated.dll.obfycache"),
            Path.Combine(src, "X.obfuscated.cs")
        ]);
        together.ShouldBe(
        [
            Path.Combine(src, "obfy-src", "X.cs"), Path.Combine(src, "obfy-src", "Y.cs"),
            Path.Combine(src, "app.obfuscated.dll"), Path.Combine(src, "app.obfuscated.dll.obfycache")
        ]);
        intoFolder.ShouldBe(
        [
            P("out", "X.cs"), P("out", "Y.cs"),
            P("out", "app.dll"), P("out", "app.dll.obfycache")
        ]);
    }

    [Fact]
    public void PlanClosedSetOutputs_UsesTheEnginesNames_AndOneFileWhenMerged()
    {
        var settings = new ObfySettings();
        var a = P("one", "bin", "A.dll");
        var b = P("two", "bin", "A.dll");
        var c = P("two", "bin", "C.dll");
        var outDir = P("out");

        var separate = Program.PlanClosedSetOutputs([a, b, c], outDir, merged: false, settings);
        var merged = Program.PlanClosedSetOutputs([a, b, c], outDir, merged: true, settings);

        // The engine puts a shared name into a folder of its own: one\bin\A.dll and two\bin\A.dll become bin\A.dll twice
        // here (its own collision error), and C.dll keeps its name.
        separate.ShouldContain(P("out", "C.dll"));
        separate.Count.ShouldBe(3);
        merged.ShouldBe([P("out", "A.dll"), P("out", "A.dll.obfycache")]);
        Program.PlanClosedSetOutputs([], outDir, merged: false, settings).ShouldBeEmpty();
    }
}
