using Autofac;
using Obfy.Core.Models;
using Obfy.Core.Models.Solution;
using Obfy.Core.Services;
using Obfy.Core.Utilities;
using Shouldly;

namespace Obfy.Tests.CoreServices;

/// <summary>
/// The incremental cache covers a single assembly and a merge. A closed set (several assemblies, a solution or a project),
/// C# source (a file, several files, a folder) never reads or writes it, so a run with <c>incremental.enabled</c> on says so,
/// once, in a warning, instead of silently doing nothing. The desktop app shows the same warning in its log: it prints the
/// warnings of every result and module result.
/// </summary>
public class CoreServicesIncrementalNotAppliedTests : IDisposable
{
    private const string ClosedSetNote =
        "Incremental cache not used: it covers a single assembly or a merge, not a closed set " +
        "(a solution, a project, or several assemblies protected together), so every assembly was obfuscated.";

    private const string SourceNote =
        "Incremental cache not used: it covers a single assembly or a merge, not C# source " +
        "(a file, several files or a folder), so every file was obfuscated.";

    private readonly string _dir = CoreServicesTestSupport.NewTempDirectory("obfy-cache-na");

    public void Dispose() => CoreServicesTestSupport.DeleteQuietly(_dir);

    private static ObfySettings Settings(bool incremental)
    {
        var settings = CoreServicesTestSupport.AllOff();
        settings.StringEncryption.Enabled = true;
        settings.Incremental.Enabled = incremental;
        return settings;
    }

    private static ClosedSetInput Input(string path) =>
        new() { AssemblyPath = path, Hints = new ProjectSettingsHints() };

    private (string A, string B) TwoLibs() =>
        (CoreServicesTestSupport.CompileLibrary(
            Path.Combine(_dir, "src"), "LibA", "public class A { public static string S() => \"a string that is long enough\"; }"),
         CoreServicesTestSupport.CompileLibrary(
            Path.Combine(_dir, "src"), "LibB", "public class B { public static string S() => \"another string that is long\"; }"));

    private string Source(string name, string body)
    {
        var folder = Path.Combine(_dir, "cs");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name);
        File.WriteAllText(path, body);
        return path;
    }

    private static int Count(IEnumerable<string> warnings, string note) => warnings.Count(w => w == note);

    private static IContainer Container(out IObfuscationService service)
    {
        var container = CoreServicesTestSupport.BuildContainer();
        service = container.Resolve<IObfuscationService>();
        return container;
    }

    // ---- Closed set -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task ClosedSet_WithIncremental_SaysItOnce_OnTheFirstModuleOnly()
    {
        using var container = Container(out var service);
        var (a, b) = TwoLibs();

        var result = await service.ObfuscateClosedSetAsync([Input(a), Input(b)], Path.Combine(_dir, "out"), Settings(incremental: true));

        result.Success.ShouldBeTrue(result.ErrorMessage);
        result.ModuleResults.Count.ShouldBe(2);
        Count(result.ModuleResults[0].Warnings, ClosedSetNote).ShouldBe(1);
        Count(result.ModuleResults[1].Warnings, ClosedSetNote).ShouldBe(0);
        result.ModuleResults.SelectMany(static m => m.Warnings).Count(static w => w.StartsWith("Incremental", StringComparison.Ordinal))
            .ShouldBe(1, "not more than once per run");
    }

    [Fact]
    public async Task ClosedSetOfOneAssembly_WithIncremental_SaysIt()
    {
        // A solution with one included project is still a closed set: the cache is not used for it.
        using var container = Container(out var service);
        var (a, _) = TwoLibs();

        var result = await service.ObfuscateClosedSetAsync([Input(a)], Path.Combine(_dir, "out"), Settings(incremental: true));

        result.Success.ShouldBeTrue(result.ErrorMessage);
        Count(result.ModuleResults.SelectMany(static m => m.Warnings), ClosedSetNote).ShouldBe(1);
    }

    [Fact]
    public async Task ClosedSet_WithoutIncremental_SaysNothingAboutTheCache()
    {
        using var container = Container(out var service);
        var (a, b) = TwoLibs();

        var result = await service.ObfuscateClosedSetAsync([Input(a), Input(b)], Path.Combine(_dir, "out"), Settings(incremental: false));

        result.Success.ShouldBeTrue(result.ErrorMessage);
        result.ModuleResults.SelectMany(static m => m.Warnings).ShouldNotContain(static w => w.Contains("ncremental", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ClosedSet_WithIncremental_WritesNoCacheFile()
    {
        using var container = Container(out var service);
        var (a, b) = TwoLibs();
        var outDir = Path.Combine(_dir, "out");

        await service.ObfuscateClosedSetAsync([Input(a), Input(b)], outDir, Settings(incremental: true));

        Directory.GetFiles(outDir, "*.obfycache").ShouldBeEmpty("the note is true: nothing is cached");
    }

    // ---- What the cache does cover: no note -----------------------------------------------------------------------

    [Fact]
    public async Task SingleAssembly_WithIncremental_NeverSaysItIsNotCovered()
    {
        using var container = Container(out var service);
        var (a, _) = TwoLibs();
        var output = Path.Combine(_dir, "out", "LibA.dll");
        var settings = Settings(incremental: true);

        var first = await service.ObfuscateAsync(a, output, settings);
        var second = await service.ObfuscateAsync(a, output, settings);

        first.Success.ShouldBeTrue(first.ErrorMessage);
        second.FromCache.ShouldBeTrue();
        foreach (var warning in first.Warnings.Concat(second.Warnings))
        {
            warning.ShouldNotBe(ClosedSetNote);
            warning.ShouldNotBe(SourceNote);
        }
    }

    [Fact]
    public async Task Merge_WithIncremental_NeverSaysItIsNotCovered()
    {
        using var container = Container(out var service);
        var (a, b) = TwoLibs();

        var result = await service.MergeAndObfuscateAsync([a, b], Path.Combine(_dir, "out", "Merged.dll"), Settings(incremental: true));

        result.Success.ShouldBeTrue(result.ErrorMessage);
        result.Warnings.ShouldNotContain(ClosedSetNote);
        result.Warnings.ShouldNotContain(SourceNote);
    }

    // ---- C# source ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task SingleSourceFile_WithIncremental_SaysItOnce()
    {
        using var container = Container(out var service);
        var cs = Source("One.cs", "class One { string S() => \"a string that is long enough\"; }");

        var result = await service.ObfuscateAsync(cs, Path.Combine(_dir, "out", "One.cs"), Settings(incremental: true));

        result.Success.ShouldBeTrue(result.ErrorMessage);
        Count(result.Warnings, SourceNote).ShouldBe(1);
        File.Exists(IncrementalCache.CachePath(Path.Combine(_dir, "out", "One.cs"))).ShouldBeFalse();
    }

    [Fact]
    public async Task SourceFolder_WithIncremental_SaysItOnce()
    {
        using var container = Container(out var service);
        Source("One.cs", "class One { string S() => \"a string that is long enough\"; }");
        Source("Two.cs", "class Two { string S() => \"another string that is long\"; }");

        var result = await service.ObfuscateAsync(Path.Combine(_dir, "cs"), Path.Combine(_dir, "out-src"), Settings(incremental: true));

        result.Success.ShouldBeTrue(result.ErrorMessage);
        Count(result.Warnings, SourceNote).ShouldBe(1);
    }

    [Fact]
    public async Task SeveralSourceFiles_WithIncremental_SaysItOnce()
    {
        using var container = Container(out var service);
        var one = Source("One.cs", "class One { string S() => \"a string that is long enough\"; }");
        var two = Source("Two.cs", "class Two { string S() => \"another string that is long\"; }");

        var result = await service.ObfuscateSourcesAsync([one, two], Path.Combine(_dir, "out-src"), Settings(incremental: true));

        result.Success.ShouldBeTrue(result.ErrorMessage);
        Count(result.Warnings, SourceNote).ShouldBe(1);
    }

    [Fact]
    public async Task Source_WithoutIncremental_SaysNothingAboutTheCache()
    {
        using var container = Container(out var service);
        var one = Source("One.cs", "class One { string S() => \"a string that is long enough\"; }");
        var two = Source("Two.cs", "class Two { string S() => \"another string that is long\"; }");

        var single = await service.ObfuscateAsync(one, Path.Combine(_dir, "out", "One.cs"), Settings(incremental: false));
        var folder = await service.ObfuscateAsync(Path.Combine(_dir, "cs"), Path.Combine(_dir, "out-folder"), Settings(incremental: false));
        var several = await service.ObfuscateSourcesAsync([one, two], Path.Combine(_dir, "out-src"), Settings(incremental: false));

        foreach (var result in new[] { single, folder, several })
        {
            result.Success.ShouldBeTrue(result.ErrorMessage);
            result.Warnings.ShouldNotContain(static w => w.Contains("ncremental", StringComparison.Ordinal));
        }
    }

    // ---- A cache that cannot be written is a warning, never an error -----------------------------------------------

    [Fact]
    public async Task CacheThatCannotBeWritten_IsAWarning_AndTheRunStillSucceeds()
    {
        using var container = Container(out var service);
        var (a, _) = TwoLibs();
        var output = Path.Combine(_dir, "out", "LibA.dll");
        Directory.CreateDirectory(IncrementalCache.CachePath(output)); // the cache file's name is taken by a folder

        var result = await service.ObfuscateAsync(a, output, Settings(incremental: true));

        result.Success.ShouldBeTrue(result.ErrorMessage);
        File.Exists(output).ShouldBeTrue();
        result.Warnings.ShouldContain(
            "Incremental cache could not be written next to the output, so the next run will not be skipped.");
        Directory.GetFiles(Path.Combine(_dir, "out"), "*.tmp").ShouldBeEmpty("no temporary file is left behind");
    }
}
