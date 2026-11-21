using System.Text.Json;
using Autofac;
using Obfy.App.Tests.Common;
using Obfy.Core.Models;
using Obfy.Core.Services;
using Obfy.Core.Utilities;
using Shouldly;

namespace Obfy.Tests.CoreServices;

/// <summary>
/// A cache hit says "this output is the protected result of that input". It must only say so for the file the cached
/// run wrote: the entry carries a fingerprint (length and SHA-256) of the output, and of the launcher files a packed
/// output needs, and a hit needs the files on disk to still match. Anything else (the unprotected original copied over
/// the output, one changed byte, a replaced launcher) is a miss and the run is done again.
/// </summary>
public class CoreServicesIncrementalOutputGuardTests : IDisposable
{
    private const string ReusedWarning = "Incremental: reused cached output";

    private const string InPlaceNote =
        "Incremental cache not used: the output replaces the input file. Write the output to a different path to use the cache.";

    private const string DllWarning =
        "Packing a .dll writes a native PE with a .dll extension; leftover apphost EXEs will not load it. Prefer packing the entry-point .exe, or rename the packed file to .exe.";

    private readonly string _dir = CoreServicesTestSupport.NewTempDirectory("obfy-cache-guard");

    public void Dispose() => CoreServicesTestSupport.DeleteQuietly(_dir);

    private static ObfySettings Incremental()
    {
        var settings = CoreServicesTestSupport.AllOff();
        settings.StringEncryption.Enabled = true;
        settings.Incremental.Enabled = true;
        return settings;
    }

    private static ObfySettings PackedIncremental(bool portable)
    {
        var settings = Incremental();
        settings.Packing.Enabled = true;
        if (portable)
            settings.Packing.Rid = "portable";
        return settings;
    }

    private string CompileLib(string folder = "src") =>
        CoreServicesTestSupport.CompileLibrary(
            Path.Combine(_dir, folder), "Lib", "public class Lib { public static string S() => \"a string that is long enough\"; }");

    private string CompileExe(string folder = "src") =>
        CoreServicesTestSupport.CompileExe(
            Path.Combine(_dir, folder), "App",
            "public static class Program { public static int Main() => \"a string that is long enough\".Length > 0 ? 11 : 0; }");

    private static IObfuscationService NewService(out IContainer container)
    {
        container = CoreServicesTestSupport.BuildContainer();
        return container.Resolve<IObfuscationService>();
    }

    // ---- The output on disk is not the output the cached run wrote ---------------------------------------------

    [Fact]
    public async Task OutputReplacedByTheUnprotectedOriginal_IsRebuilt_NotReusedAsProtected()
    {
        var service = NewService(out var container);
        using var _ = container;
        var input = CompileLib();
        var original = await File.ReadAllBytesAsync(input);
        var output = Path.Combine(_dir, "out", "Lib.dll");
        var settings = Incremental();

        var first = await service.ObfuscateAsync(input, output, settings);
        first.Success.ShouldBeTrue(first.ErrorMessage);
        File.Exists(IncrementalCache.CachePath(output)).ShouldBeTrue();

        // A build copies the unprotected original over the output. The cache file and its key are untouched.
        await File.WriteAllBytesAsync(output, original);
        IncrementalCache.TryHit(input, output, settings).ShouldBeFalse("the file on disk is not what the cached run wrote");
        var second = await service.ObfuscateAsync(input, output, settings);

        second.Success.ShouldBeTrue(second.ErrorMessage);
        second.Warnings.ShouldNotContain(w => w.StartsWith(ReusedWarning, StringComparison.Ordinal));
        second.FromCache.ShouldBeFalse();
        (await File.ReadAllBytesAsync(output)).ShouldNotBe(original, "the run protected the file again");
        second.Statistics.TotalTransformations.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task OutputModifiedByOneByte_IsRebuilt()
    {
        var service = NewService(out var container);
        using var _ = container;
        var input = CompileLib();
        var output = Path.Combine(_dir, "out", "Lib.dll");
        var settings = Incremental();
        await service.ObfuscateAsync(input, output, settings);

        var bytes = await File.ReadAllBytesAsync(output);
        bytes[^1] ^= 0xFF;
        await File.WriteAllBytesAsync(output, bytes);
        IncrementalCache.TryHit(input, output, settings).ShouldBeFalse();
        var second = await service.ObfuscateAsync(input, output, settings);

        second.Success.ShouldBeTrue(second.ErrorMessage);
        second.FromCache.ShouldBeFalse();
        (await File.ReadAllBytesAsync(output)).ShouldNotBe(bytes, "the damaged output was replaced by a fresh one");
    }

    [Fact]
    public async Task OutputWithAnotherLength_IsRebuilt()
    {
        var service = NewService(out var container);
        using var _ = container;
        var input = CompileLib();
        var output = Path.Combine(_dir, "out", "Lib.dll");
        var settings = Incremental();
        await service.ObfuscateAsync(input, output, settings);

        await File.AppendAllTextAsync(output, "x");

        IncrementalCache.TryHit(input, output, settings).ShouldBeFalse();
        (await service.ObfuscateAsync(input, output, settings)).FromCache.ShouldBeFalse();
    }

    [Fact]
    public async Task UntouchedOutput_StillHits_AndIsNotWrittenAgain()
    {
        var service = NewService(out var container);
        using var _ = container;
        var input = CompileLib();
        var output = Path.Combine(_dir, "out", "Lib.dll");
        var settings = Incremental();
        await service.ObfuscateAsync(input, output, settings);
        var stamp = File.GetLastWriteTimeUtc(output);
        var bytes = await File.ReadAllBytesAsync(output);

        var second = await service.ObfuscateAsync(input, output, settings);

        second.FromCache.ShouldBeTrue();
        File.GetLastWriteTimeUtc(output).ShouldBe(stamp);
        (await File.ReadAllBytesAsync(output)).ShouldBe(bytes);
    }

    [Fact]
    public async Task CacheFile_CarriesAFingerprintOfTheOutputAndNothingThatNamesIt()
    {
        var service = NewService(out var container);
        using var _ = container;
        var input = CompileLib();
        var output = Path.Combine(_dir, "out", "Lib.dll");
        await service.ObfuscateAsync(input, output, Incremental());

        using var entry = JsonDocument.Parse(await File.ReadAllBytesAsync(IncrementalCache.CachePath(output)));
        var root = entry.RootElement;
        root.EnumerateObject().Select(p => p.Name).ShouldBe(
            ["format", "key", "statistics", "symbolMapEntries", "skippedItems", "warnings", "output", "companions"]);
        root.GetProperty("format").GetInt32().ShouldBe(3);
        var fingerprint = root.GetProperty("output");
        fingerprint.EnumerateObject().Select(p => p.Name).ShouldBe(["length", "sha256"]);
        fingerprint.GetProperty("length").GetInt64().ShouldBe(new FileInfo(output).Length);
        fingerprint.GetProperty("sha256").GetString().ShouldBe(
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(output))));
        root.GetProperty("companions").GetArrayLength().ShouldBe(0, "nothing is packed");
    }

    [Fact]
    public async Task CacheFileOfTheFormatBeforeTheFingerprint_IsAMiss()
    {
        var service = NewService(out var container);
        using var _ = container;
        var input = CompileLib();
        var output = Path.Combine(_dir, "out", "Lib.dll");
        var settings = Incremental();
        await service.ObfuscateAsync(input, output, settings);
        var key = IncrementalCache.ComputeKey(input, settings);
        await File.WriteAllTextAsync(
            IncrementalCache.CachePath(output),
            "{\"format\":2,\"key\":\"" + key + "\",\"statistics\":{\"stringsEncrypted\":1},\"symbolMapEntries\":0,\"skippedItems\":0,\"warnings\":0}");

        IncrementalCache.TryHit(input, output, settings).ShouldBeFalse("it vouches for nothing about the output file");
        (await service.ObfuscateAsync(input, output, settings)).FromCache.ShouldBeFalse();
    }

    // ---- The output is the input, reached through another spelling ---------------------------------------------

    [JunctionFact]
    public async Task OutputReachedThroughAJunctionToTheInputsFolder_IsInPlace_NotAStaleHit()
    {
        var service = NewService(out var container);
        using var _ = container;
        var source = CompileLib();
        var real = Path.Combine(_dir, "real");
        Directory.CreateDirectory(real);
        var link = Path.Combine(_dir, "link");
        Junctions.TryCreate(link, real, out var error).ShouldBeTrue(error);
        var target = Path.Combine(real, "Lib.dll");
        var settings = Incremental();

        // A separate-output run leaves real\Lib.dll protected, with real\Lib.dll.obfycache keyed on the original.
        (await service.ObfuscateAsync(source, target, settings)).Success.ShouldBeTrue();
        // A build restores the original bytes. The post-build step is: obfy real\Lib.dll -o link
        var original = await File.ReadAllBytesAsync(source);
        await File.WriteAllBytesAsync(target, original);

        var result = await service.ObfuscateAsync(target, Path.Combine(link, "Lib.dll"), settings);

        result.Success.ShouldBeTrue(result.ErrorMessage);
        result.Warnings.ShouldNotContain(w => w.StartsWith(ReusedWarning, StringComparison.Ordinal));
        result.Warnings.ShouldContain(InPlaceNote);
        result.Warnings.ShouldNotContain(w => w.Contains("changed while the run was in progress", StringComparison.Ordinal));
        (await File.ReadAllBytesAsync(target)).ShouldNotBe(original, "the in-place run protected the file");
        File.Exists(IncrementalCache.CachePath(target)).ShouldBeFalse("the stale cache is removed and none is written");
    }

    [JunctionFact]
    public async Task InputReachedThroughAJunction_AndTheOutputInTheRealFolder_IsInPlace()
    {
        var service = NewService(out var container);
        using var _ = container;
        var real = Path.Combine(_dir, "real");
        Directory.CreateDirectory(real);
        var link = Path.Combine(_dir, "link");
        Junctions.TryCreate(link, real, out var error).ShouldBeTrue(error);
        File.Copy(CompileLib(), Path.Combine(real, "Lib.dll"));

        var result = await service.ObfuscateAsync(Path.Combine(link, "Lib.dll"), Path.Combine(real, "Lib.dll"), Incremental());

        result.Success.ShouldBeTrue(result.ErrorMessage);
        result.Warnings.ShouldContain(InPlaceNote);
        File.Exists(IncrementalCache.CachePath(Path.Combine(real, "Lib.dll"))).ShouldBeFalse();
    }

    [Fact]
    public async Task OutputSpelledWithTheExtendedLengthPrefix_IsInPlace()
    {
        if (!OperatingSystem.IsWindows())
            return; // \\?\ is a Windows spelling

        var service = NewService(out var container);
        using var _ = container;
        var input = CompileLib();

        var result = await service.ObfuscateAsync(input, @"\\?\" + input, Incremental());

        result.Success.ShouldBeTrue(result.ErrorMessage);
        result.Warnings.ShouldContain(InPlaceNote);
        result.Warnings.ShouldNotContain(w => w.Contains("changed while the run was in progress", StringComparison.Ordinal));
        File.Exists(IncrementalCache.CachePath(input)).ShouldBeFalse();
    }

    // ---- Packed outputs: the files a hit vouches for ----------------------------------------------------------

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PackedOutput_Untouched_StillHits(bool portable)
    {
        var service = NewService(out var container);
        using var _ = container;
        var input = CompileExe();
        var output = Path.Combine(_dir, "out", "App.exe");
        var settings = PackedIncremental(portable);
        (await service.ObfuscateAsync(input, output, settings)).Success.ShouldBeTrue();

        var second = await service.ObfuscateAsync(input, output, settings);

        second.FromCache.ShouldBeTrue();
        using var entry = JsonDocument.Parse(await File.ReadAllBytesAsync(IncrementalCache.CachePath(output)));
        entry.RootElement.GetProperty("companions").GetArrayLength().ShouldBe(portable ? 2 : 1);
    }

    [Theory]
    [InlineData(true, "launcher")]
    [InlineData(true, "runtimeconfig")]
    [InlineData(false, "runtimeconfig")]
    public async Task PackedOutput_WithAReplacedSidecar_IsRebuilt(bool portable, string which)
    {
        var service = NewService(out var container);
        using var _ = container;
        var input = CompileExe();
        var output = Path.Combine(_dir, "out", "App.exe");
        var settings = PackedIncremental(portable);
        (await service.ObfuscateAsync(input, output, settings)).Success.ShouldBeTrue();
        var sidecar = which == "launcher"
            ? ManagedLauncherPacker.LauncherPathFor(output)
            : portable ? ManagedLauncherPacker.RuntimeConfigPathFor(output) : NativePacker.RuntimeConfigPathFor(output);
        var good = await File.ReadAllBytesAsync(sidecar);
        byte[] tampered = [.. good, (byte)' '];
        await File.WriteAllBytesAsync(sidecar, tampered);

        IncrementalCache.TryHit(input, output, settings).ShouldBeFalse("a launcher file that is not the one the cached run wrote");
        var second = await service.ObfuscateAsync(input, output, settings);

        second.Success.ShouldBeTrue(second.ErrorMessage);
        second.FromCache.ShouldBeFalse();
        (await File.ReadAllBytesAsync(sidecar)).ShouldNotBe(tampered, "the run wrote the file again");
    }

    [Fact]
    public async Task PackedOutput_WithAMissingLauncher_IsRebuilt()
    {
        var service = NewService(out var container);
        using var _ = container;
        var input = CompileExe();
        var output = Path.Combine(_dir, "out", "App.exe");
        var settings = PackedIncremental(portable: true);
        (await service.ObfuscateAsync(input, output, settings)).Success.ShouldBeTrue();
        File.Delete(ManagedLauncherPacker.LauncherPathFor(output));

        var second = await service.ObfuscateAsync(input, output, settings);

        second.FromCache.ShouldBeFalse();
        File.Exists(ManagedLauncherPacker.LauncherPathFor(output)).ShouldBeTrue();
    }

    // ---- Warnings a hit repeats, and the ones it counts --------------------------------------------------------

    [Fact]
    public async Task NativePackedDll_HitRepeatsTheDllWarning_AndCountsNoWarningAsLost()
    {
        var service = NewService(out var container);
        using var _ = container;
        var input = CompileExe();
        var output = Path.Combine(_dir, "out", "App.packed.dll");
        var settings = PackedIncremental(portable: false);

        var first = await service.ObfuscateAsync(input, output, settings);
        var second = await service.ObfuscateAsync(input, output, settings);

        first.Success.ShouldBeTrue(first.ErrorMessage);
        first.Warnings.ShouldContain(DllWarning);
        second.FromCache.ShouldBeTrue();
        second.Warnings.ShouldContain(w => w.StartsWith("Packed native host:", StringComparison.Ordinal));
        second.Warnings.ShouldContain(DllWarning, "a deterministic advisory is repeated, not counted as lost");
        second.Warnings.ShouldNotContain(w => w.StartsWith("The run that produced this output raised", StringComparison.Ordinal));
        using var entry = JsonDocument.Parse(await File.ReadAllBytesAsync(IncrementalCache.CachePath(output)));
        entry.RootElement.GetProperty("warnings").GetInt32().ShouldBe(0);
    }

    // ---- A caller that builds a report -------------------------------------------------------------------------

    private static readonly ObfuscationRunOptions NeedsReport = new() { NeedsReport = true };
    private static readonly ObfuscationRunOptions NeedsMap = new() { NeedsSymbolMap = true };

    // Virtualization skips the method with an exception handler (a skipped item that names it) and, with maxMethods = 1,
    // raises a warning. Renaming is off, so the symbol map of the run is empty.
    private string CompileSkippingSample() =>
        CoreServicesTestSupport.CompileLibrary(
            Path.Combine(_dir, "src"), "Lib",
            "public class Gizmo { " +
            "public int UnicornTryMethod(int x) { try { return x / (x - 1); } catch (System.DivideByZeroException) { return -1; } } " +
            "public int PlainOne(int x) => x * 3 + 1; " +
            "public int PlainTwo(int x) => x * 5 + 2; }");

    private static ObfySettings SkippingSettings()
    {
        var settings = Incremental();
        settings.Virtualization.Enabled = true;
        settings.Virtualization.MaxMethods = 1;
        return settings;
    }

    [Fact]
    public async Task RunThatNeedsAReport_DoesTheWorkAgain_WhenTheCachedRunSkippedItemsOrRaisedWarnings()
    {
        var service = NewService(out var container);
        using var _ = container;
        var input = CompileSkippingSample();
        var output = Path.Combine(_dir, "out", "Lib.dll");
        var settings = SkippingSettings();
        var first = await service.ObfuscateAsync(input, output, settings);
        first.SkippedItems.ShouldNotBeEmpty("the sample must give the cached run something a hit cannot repeat");

        var second = await service.ObfuscateAsync(input, output, settings, NeedsReport);

        second.Success.ShouldBeTrue(second.ErrorMessage);
        second.FromCache.ShouldBeFalse();
        second.SkippedItems.ShouldNotBeEmpty("the report can list them again");
        second.Warnings.ShouldNotContain(w => w.StartsWith(ReusedWarning, StringComparison.Ordinal));
        second.Warnings.ShouldContain(w => w.StartsWith("Incremental cache not used for this run: the report is needed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunThatOnlyNeedsTheSymbolMap_StillHits_WhenTheCachedRunSkippedItemsButRenamedNothing()
    {
        var service = NewService(out var container);
        using var _ = container;
        var input = CompileSkippingSample();
        var output = Path.Combine(_dir, "out", "Lib.dll");
        var settings = SkippingSettings();
        await service.ObfuscateAsync(input, output, settings);

        var second = await service.ObfuscateAsync(input, output, settings, NeedsMap);

        second.FromCache.ShouldBeTrue("an empty map is exact, and skipped items are only counted for a caller that asked for them");
        second.Warnings.ShouldContain(w => w.StartsWith("The run that produced this output skipped", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunThatNeedsAReport_DoesTheWorkAgain_WhenSymbolsWereRenamed()
    {
        var service = NewService(out var container);
        using var _ = container;
        var input = CompileLib();
        var output = Path.Combine(_dir, "out", "Lib.dll");
        var settings = Incremental();
        settings.SymbolRenaming.Enabled = true;
        await service.ObfuscateAsync(input, output, settings);

        var second = await service.ObfuscateAsync(input, output, settings, NeedsReport);

        second.FromCache.ShouldBeFalse();
        second.SymbolMap.ShouldNotBeEmpty();
        second.Warnings.ShouldContain(w => w.StartsWith("Incremental cache not used for this run: the report is needed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunThatNeedsAReport_StillHits_WhenTheCachedRunHasNothingAHitCannotRepeat()
    {
        var service = NewService(out var container);
        using var _ = container;
        var input = CompileLib();
        var output = Path.Combine(_dir, "out", "Lib.dll");
        var settings = Incremental();
        var first = await service.ObfuscateAsync(input, output, settings);
        first.SymbolMap.ShouldBeEmpty();
        first.SkippedItems.ShouldBeEmpty();
        first.Warnings.ShouldBeEmpty("the sample must be a run with nothing to repeat");

        var second = await service.ObfuscateAsync(input, output, settings, NeedsReport);

        second.FromCache.ShouldBeTrue();
        second.Statistics.TotalTransformations.ShouldBe(first.Statistics.TotalTransformations);
    }
}
