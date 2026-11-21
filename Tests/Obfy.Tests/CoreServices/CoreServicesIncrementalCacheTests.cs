using System.Text;
using System.Text.Json;
using Autofac;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Obfy.Core.Models;
using Obfy.Core.Pipeline;
using Obfy.Core.Services;
using Obfy.Core.Services.Solution;
using Obfy.Core.Utilities;
using Shouldly;

namespace Obfy.Tests.CoreServices;

/// <summary>Incremental cache correctness through the real service (key consistency, stale files, write failures).</summary>
public class CoreServicesIncrementalCacheTests : IDisposable
{
    private const string ReusedWarning = "Incremental: reused cached output";

    private const string InPlaceNote =
        "Incremental cache not used: the output replaces the input file. Write the output to a different path to use the cache.";

    private readonly string _dir = CoreServicesTestSupport.NewTempDirectory("obfy-cache-svc");

    public void Dispose() => CoreServicesTestSupport.DeleteQuietly(_dir);

    private static ObfySettings Incremental()
    {
        var settings = CoreServicesTestSupport.AllOff();
        settings.StringEncryption.Enabled = true;
        settings.Incremental.Enabled = true;
        return settings;
    }

    private string CompileInput(string name = "Lib", string body = "public static string S() => \"hello world\";")
        => CoreServicesTestSupport.CompileLibrary(_dir, name, $"public class C {{ {body} }}");

    [Fact]
    public async Task GatedRuntimeProfile_SecondRunHitsTheCache()
    {
        using var container = CoreServicesTestSupport.BuildContainer();
        var service = container.Resolve<IObfuscationService>();
        var input = CompileInput();
        var output = Path.Combine(_dir, "Lib.obfuscated.dll");
        var settings = Incremental();
        settings.RuntimeProfile = RuntimeProfile.NativeAot;
        // Each of these is switched off by the NativeAOT profile gate, so the settings the pipeline runs with
        // differ from the settings the caller passed in.
        settings.Protection.MethodEncryption = true;
        settings.Protection.AntiDump = true;
        settings.Packing.Enabled = true;

        var first = await service.ObfuscateAsync(input, output, settings);
        var second = await service.ObfuscateAsync(input, output, settings);

        first.Success.ShouldBeTrue(first.ErrorMessage);
        first.Warnings.ShouldNotContain(ReusedWarning);
        second.Success.ShouldBeTrue(second.ErrorMessage);
        second.Warnings.ShouldContain(w => w.StartsWith(ReusedWarning, StringComparison.Ordinal));
    }

    [Fact]
    public async Task GatedRuntimeProfile_CacheHitStillReportsTheGatingWarnings()
    {
        using var container = CoreServicesTestSupport.BuildContainer();
        var service = container.Resolve<IObfuscationService>();
        var input = CompileInput();
        var output = Path.Combine(_dir, "Lib.obfuscated.dll");
        var settings = Incremental();
        settings.RuntimeProfile = RuntimeProfile.NativeAot;
        settings.Protection.AntiDump = true;

        await service.ObfuscateAsync(input, output, settings);
        var second = await service.ObfuscateAsync(input, output, settings);

        second.Warnings.ShouldContain(ReusedWarning);
        second.Warnings.ShouldContain(w => w.Contains("Anti-dump disabled", StringComparison.Ordinal));
    }

    [Fact]
    public void ComputeKey_IncludesTheBuildIdentityOfObfyCore_NotJustTheVersion()
    {
        var input = CompileInput();
        var settings = Incremental();

        IncrementalCache.BuildIdentity.ShouldContain(
            typeof(IncrementalCache).Module.ModuleVersionId.ToString("N"),
            Case.Insensitive,
            "the pinned 2.0.0 assembly version does not change on a hotfix, the MVID does");
        var current = IncrementalCache.ComputeKey(input, settings);
        var hotfixed = IncrementalCache.ComputeKey(input, settings, "obfy-core-2.0.0|another-mvid");

        hotfixed.ShouldNotBe(current);
    }

    [Fact]
    public async Task FailedOverwrite_RemovesTheStaleCache_SoRevertedInputsDoNotHitABadOutput()
    {
        using var container = CoreServicesTestSupport.BuildContainer();
        var real = container.Resolve<IAssemblyProcessor>();
        var service = NewService(container, real);
        var input = CompileInput();
        var v1 = await File.ReadAllBytesAsync(input);
        var output = Path.Combine(_dir, "Lib.obfuscated.dll");
        var settings = Incremental();

        (await service.ObfuscateAsync(input, output, settings)).Success.ShouldBeTrue();
        File.Exists(IncrementalCache.CachePath(output)).ShouldBeTrue();

        // Inputs change and the run dies after it has started overwriting the output.
        CompileInput(body: "public static string S() => \"changed input\";");
        var failing = new Mock<IAssemblyProcessor>();
        failing
            .Setup(p => p.LoadAsync(It.IsAny<string>(), It.IsAny<ObfySettings>(), It.IsAny<CancellationToken>()))
            .Returns((string path, ObfySettings s, CancellationToken ct) => real.LoadAsync(path, s, ct));
        failing
            .Setup(p => p.SaveAsync(It.IsAny<PipelineContext>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((PipelineContext c, string path, CancellationToken ct) =>
            {
                File.WriteAllBytes(path, [0xDE, 0xAD]);
                throw new IOException("disk full");
            });
        var failed = await NewService(container, failing.Object).ObfuscateAsync(input, output, settings);
        failed.Success.ShouldBeFalse();

        // The user reverts to the original inputs. The output is the partial one, the old cache must not bless it.
        await File.WriteAllBytesAsync(input, v1);
        File.Exists(IncrementalCache.CachePath(output)).ShouldBeFalse("the cache must go before the output is overwritten");
        IncrementalCache.TryHit(input, output, settings).ShouldBeFalse();
        var rerun = await service.ObfuscateAsync(input, output, settings);
        rerun.Success.ShouldBeTrue(rerun.ErrorMessage);
        rerun.Warnings.ShouldNotContain(w => w.StartsWith(ReusedWarning, StringComparison.Ordinal));
        new FileInfo(output).Length.ShouldBeGreaterThan(2);
    }

    [Fact]
    public async Task RunWithoutIncremental_StillRemovesTheStaleCacheItOverwrites()
    {
        using var container = CoreServicesTestSupport.BuildContainer();
        var service = container.Resolve<IObfuscationService>();
        var input = CompileInput();
        var output = Path.Combine(_dir, "Lib.obfuscated.dll");

        (await service.ObfuscateAsync(input, output, Incremental())).Success.ShouldBeTrue();
        File.Exists(IncrementalCache.CachePath(output)).ShouldBeTrue();

        var plain = Incremental();
        plain.Incremental.Enabled = false;
        (await service.ObfuscateAsync(input, output, plain)).Success.ShouldBeTrue();

        File.Exists(IncrementalCache.CachePath(output)).ShouldBeFalse();
    }

    [Fact]
    public async Task CacheFileCannotBeWritten_ResultCarriesAWarning()
    {
        using var container = CoreServicesTestSupport.BuildContainer();
        var service = container.Resolve<IObfuscationService>();
        var input = CompileInput();
        var output = Path.Combine(_dir, "Lib.obfuscated.dll");
        var settings = Incremental();
        (await service.ObfuscateAsync(input, output, settings)).Success.ShouldBeTrue();

        using var locked = new FileStream(
            IncrementalCache.CachePath(output), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var result = await service.ObfuscateAsync(input, output, settings);

        result.Success.ShouldBeTrue(result.ErrorMessage);
        result.Warnings.ShouldContain(w => w.Contains("cache", StringComparison.OrdinalIgnoreCase)
                                           && !w.StartsWith(ReusedWarning, StringComparison.Ordinal));
    }

    [Fact]
    public async Task InputChangesWhileTheRunIsInFlight_TheCacheIsNotWritten()
    {
        using var container = CoreServicesTestSupport.BuildContainer();
        var real = container.Resolve<IAssemblyProcessor>();
        var input = CompileInput();
        var original = await File.ReadAllBytesAsync(input);
        var output = Path.Combine(_dir, "Lib.obfuscated.dll");
        var settings = Incremental();

        var racing = new Mock<IAssemblyProcessor>();
        racing
            .Setup(p => p.LoadAsync(It.IsAny<string>(), It.IsAny<ObfySettings>(), It.IsAny<CancellationToken>()))
            .Returns((string path, ObfySettings s, CancellationToken ct) => real.LoadAsync(path, s, ct));
        racing
            .Setup(p => p.SaveAsync(It.IsAny<PipelineContext>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((PipelineContext c, string path, CancellationToken ct) =>
            {
                // A build rewrites the input after it was loaded and before the run finishes.
                File.WriteAllBytes(input, [.. original, 0, 0, 0, 0]);
                return real.SaveAsync(c, path, ct);
            });

        var result = await NewService(container, racing.Object).ObfuscateAsync(input, output, settings);

        result.Success.ShouldBeTrue(result.ErrorMessage);
        // The output was built from the original bytes. A key computed from whatever the file holds after the
        // run would bless this output for the edited input.
        IncrementalCache.TryHit(input, output, settings).ShouldBeFalse();
        result.Warnings.ShouldContain(w => w.Contains("cache", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task InputChangesWhileTheRunIsInFlight_WithASeparateOutput_WarnsAndWritesNoCacheFile()
    {
        using var container = CoreServicesTestSupport.BuildContainer();
        var real = container.Resolve<IAssemblyProcessor>();
        var input = CompileInput();
        var original = await File.ReadAllBytesAsync(input);
        var output = Path.Combine(_dir, "Lib.obfuscated.dll");

        var racing = new Mock<IAssemblyProcessor>();
        racing
            .Setup(p => p.LoadAsync(It.IsAny<string>(), It.IsAny<ObfySettings>(), It.IsAny<CancellationToken>()))
            .Returns((string path, ObfySettings s, CancellationToken ct) => real.LoadAsync(path, s, ct));
        racing
            .Setup(p => p.SaveAsync(It.IsAny<PipelineContext>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((PipelineContext c, string path, CancellationToken ct) =>
            {
                File.WriteAllBytes(input, [.. original, 0, 0, 0, 0]);
                return real.SaveAsync(c, path, ct);
            });

        var result = await NewService(container, racing.Object).ObfuscateAsync(input, output, Incremental());

        result.Success.ShouldBeTrue(result.ErrorMessage);
        result.Warnings.ShouldContain(w => w.Contains("changed while the run was in progress", StringComparison.Ordinal));
        File.Exists(IncrementalCache.CachePath(output)).ShouldBeFalse();
    }

    [Fact]
    public async Task InPlaceRun_WritesNoCacheAndDoesNotClaimTheInputChangedDuringTheRun()
    {
        using var container = CoreServicesTestSupport.BuildContainer();
        var service = container.Resolve<IObfuscationService>();
        var input = CompileInput();
        var before = await File.ReadAllBytesAsync(input);

        // The documented MSBuild step: obfy $(TargetPath) -o $(TargetDir), so the output replaces the input.
        var result = await service.ObfuscateAsync(input, input, Incremental());

        result.Success.ShouldBeTrue(result.ErrorMessage);
        (await File.ReadAllBytesAsync(input)).ShouldNotBe(before, "the output replaces the input");
        result.Warnings.ShouldNotContain(w => w.Contains("changed while the run was in progress", StringComparison.Ordinal));
        File.Exists(IncrementalCache.CachePath(input)).ShouldBeFalse("a cache keyed on the replaced input could never hit");
        result.Warnings.Count(w => w == InPlaceNote).ShouldBe(1, "said once, with the exact text");
    }

    [Fact]
    public async Task InPlaceRun_ReachedThroughADifferentSpellingOfThePath_IsStillInPlace()
    {
        using var container = CoreServicesTestSupport.BuildContainer();
        var service = container.Resolve<IObfuscationService>();
        var input = CompileInput();
        Directory.CreateDirectory(Path.Combine(_dir, "sub"));
        // Same file, spelled with a detour through another folder. On Windows also in another case.
        var spelled = Path.Combine(_dir, "sub", "..", "Lib.dll");
        if (OperatingSystem.IsWindows())
            spelled = Path.Combine(_dir.ToUpperInvariant(), "sub", "..", "LIB.DLL");

        var result = await service.ObfuscateAsync(input, spelled, Incremental());

        result.Success.ShouldBeTrue(result.ErrorMessage);
        result.Warnings.ShouldNotContain(w => w.Contains("changed while the run was in progress", StringComparison.Ordinal));
        result.Warnings.ShouldContain(InPlaceNote);
        File.Exists(IncrementalCache.CachePath(input)).ShouldBeFalse();
        File.Exists(IncrementalCache.CachePath(spelled)).ShouldBeFalse();
    }

    [Fact]
    public async Task InPlaceRun_WithoutIncrementalMode_SaysNothingAboutTheCache()
    {
        using var container = CoreServicesTestSupport.BuildContainer();
        var service = container.Resolve<IObfuscationService>();
        var input = CompileInput();
        var settings = Incremental();
        settings.Incremental.Enabled = false;

        var result = await service.ObfuscateAsync(input, input, settings);

        result.Success.ShouldBeTrue(result.ErrorMessage);
        result.Warnings.ShouldNotContain(w => w.Contains("cache", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SeparateOutput_DoesNotGetTheInPlaceNote_AndStillCaches()
    {
        using var container = CoreServicesTestSupport.BuildContainer();
        var service = container.Resolve<IObfuscationService>();
        var input = CompileInput();
        var output = Path.Combine(_dir, "Lib.obfuscated.dll");

        var result = await service.ObfuscateAsync(input, output, Incremental());

        result.Success.ShouldBeTrue(result.ErrorMessage);
        result.Warnings.ShouldNotContain(InPlaceNote);
        File.Exists(IncrementalCache.CachePath(output)).ShouldBeTrue();
    }

    [Fact]
    public async Task InPlaceRun_NeverTakesACacheHit_EvenWhenAStaleCacheMatchesTheFileOnDisk()
    {
        using var container = CoreServicesTestSupport.BuildContainer();
        var service = container.Resolve<IObfuscationService>();
        var source = CompileInput();
        var original = await File.ReadAllBytesAsync(source);
        var folder = Path.Combine(_dir, "bin");
        Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, "Lib.dll");

        // A separate-output run leaves bin\Lib.dll.obfycache keyed on the original bytes.
        (await service.ObfuscateAsync(source, target, Incremental())).Success.ShouldBeTrue();
        File.Exists(IncrementalCache.CachePath(target)).ShouldBeTrue();

        // A build then puts the very same original bytes there, and the post-build step runs in place. The key of
        // the file on disk equals the stored key, but the file is the unprotected input, not a cached output.
        await File.WriteAllBytesAsync(target, original);
        var result = await service.ObfuscateAsync(target, target, Incremental());

        result.Success.ShouldBeTrue(result.ErrorMessage);
        result.Warnings.ShouldNotContain(w => w.StartsWith(ReusedWarning, StringComparison.Ordinal));
        (await File.ReadAllBytesAsync(target)).ShouldNotBe(original, "the in-place run protected the file instead of reusing it");
    }

    // ---- What a cache hit returns (B-49) ----------------------------------------------------------------------

    // Names nobody would pick by accident, so a byte search of the cache file proves they are not in it.
    private static readonly string[] OriginalNames =
    [
        "Zephyr", "Quill", "LedgerKeeper", "_quokkaBalance", "WombatGreeting", "FerretTotal",
        "hello wombat greeting literal"
    ];

    private const string DistinctiveSource =
        "namespace Zephyr.Quill { public class LedgerKeeper { private int _quokkaBalance; " +
        "public string WombatGreeting() => \"hello wombat greeting literal\"; " +
        "public int FerretTotal(int amount) { _quokkaBalance += amount; return _quokkaBalance + 1234567; } } }";

    /// <summary>Strings and renaming on, with deterministic names so two runs can be compared.</summary>
    private static ObfySettings RenamingIncremental()
    {
        var settings = Incremental();
        settings.SymbolRenaming.Enabled = true;
        settings.SymbolRenaming.Mode = NamingMode.Sequential;
        return settings;
    }

    private string CompileDistinctive() =>
        CoreServicesTestSupport.CompileLibrary(_dir, "Lib", DistinctiveSource);

    private static ObfuscationRunOptions NeedsMap { get; } = new() { NeedsSymbolMap = true };

    [Fact]
    public async Task SecondRun_ReturnsTheStatisticsOfTheRunThatWroteTheOutput_AndIsMarkedAsCached()
    {
        using var container = CoreServicesTestSupport.BuildContainer();
        var service = container.Resolve<IObfuscationService>();
        var input = CompileDistinctive();
        var output = Path.Combine(_dir, "Lib.obfuscated.dll");
        var settings = RenamingIncremental();

        var first = await service.ObfuscateAsync(input, output, settings);
        var second = await service.ObfuscateAsync(input, output, settings);

        first.Success.ShouldBeTrue(first.ErrorMessage);
        first.FromCache.ShouldBeFalse();
        first.Statistics.TotalTransformations.ShouldBeGreaterThan(0, "the sample must give the run something to count");
        second.Success.ShouldBeTrue(second.ErrorMessage);
        second.Warnings.ShouldContain(w => w.StartsWith(ReusedWarning, StringComparison.Ordinal));
        JsonSerializer.Serialize(second.Statistics).ShouldBe(JsonSerializer.Serialize(first.Statistics));
        second.Statistics.TotalTransformations.ShouldBe(first.Statistics.TotalTransformations);
        second.FromCache.ShouldBeTrue();
        second.OutputPath.ShouldBe(output);
    }

    [Fact]
    public async Task SecondRun_ThatNeedsTheSymbolMap_DoesTheWorkAgain_AndReturnsTheFirstRunsMap()
    {
        using var container = CoreServicesTestSupport.BuildContainer();
        var service = container.Resolve<IObfuscationService>();
        var input = CompileDistinctive();
        var output = Path.Combine(_dir, "Lib.obfuscated.dll");
        var settings = RenamingIncremental();

        var first = await service.ObfuscateAsync(input, output, settings, NeedsMap);
        var second = await service.ObfuscateAsync(input, output, settings, NeedsMap);

        first.SymbolMap.ShouldNotBeEmpty();
        second.Success.ShouldBeTrue(second.ErrorMessage);
        second.FromCache.ShouldBeFalse("a hit cannot supply the map, so the run is done again");
        second.SymbolMap.ShouldBe(first.SymbolMap);
        second.Warnings.ShouldNotContain(w => w.StartsWith(ReusedWarning, StringComparison.Ordinal));
        second.Warnings.ShouldContain(w => w.StartsWith("Incremental cache not used for this run: the symbol map", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunThatDoesNotNeedTheMap_StillHits_AfterARunThatDid()
    {
        using var container = CoreServicesTestSupport.BuildContainer();
        var service = container.Resolve<IObfuscationService>();
        var input = CompileDistinctive();
        var output = Path.Combine(_dir, "Lib.obfuscated.dll");
        var settings = RenamingIncremental();

        await service.ObfuscateAsync(input, output, settings, NeedsMap);
        await service.ObfuscateAsync(input, output, settings, NeedsMap);
        var plain = await service.ObfuscateAsync(input, output, settings);

        plain.FromCache.ShouldBeTrue("the run that redid the work rewrote the cache for the next caller");
        plain.Statistics.TotalTransformations.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task SecondRun_ThatNeedsTheSymbolMap_StillHits_WhenTheCachedRunRenamedNothing()
    {
        using var container = CoreServicesTestSupport.BuildContainer();
        var service = container.Resolve<IObfuscationService>();
        var input = CompileDistinctive();
        var output = Path.Combine(_dir, "Lib.obfuscated.dll");
        var settings = Incremental(); // renaming is off: the map of this run is empty, and so is the map of a hit

        var first = await service.ObfuscateAsync(input, output, settings, NeedsMap);
        var second = await service.ObfuscateAsync(input, output, settings, NeedsMap);

        first.SymbolMap.ShouldBeEmpty();
        second.FromCache.ShouldBeTrue();
        second.SymbolMap.ShouldBeEmpty();
        second.Statistics.StringsEncrypted.ShouldBe(first.Statistics.StringsEncrypted);
        second.Warnings.ShouldNotContain(w => w.Contains("not used", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CacheFile_HoldsCountsOnly_AndNoneOfTheOriginalNames()
    {
        using var container = CoreServicesTestSupport.BuildContainer();
        var service = container.Resolve<IObfuscationService>();
        var input = CompileDistinctive();
        var output = Path.Combine(_dir, "Lib.obfuscated.dll");

        var first = await service.ObfuscateAsync(input, output, RenamingIncremental(), NeedsMap);

        first.SymbolMap.Keys.ShouldContain(k => k.Contains("LedgerKeeper", StringComparison.Ordinal),
            "the run knows the original names; the point is that the cache must not");
        var bytes = await File.ReadAllBytesAsync(IncrementalCache.CachePath(output));
        bytes.Length.ShouldBeGreaterThan(0);
        foreach (var name in OriginalNames)
        {
            ContainsBytes(bytes, Encoding.UTF8.GetBytes(name)).ShouldBeFalse($"'{name}' (UTF-8) is in the cache file");
            ContainsBytes(bytes, Encoding.Unicode.GetBytes(name)).ShouldBeFalse($"'{name}' (UTF-16) is in the cache file");
        }

        // And the shape itself: the key, the statistics (numbers) and three counts, nothing else.
        using var document = JsonDocument.Parse(bytes);
        document.RootElement.EnumerateObject().Select(p => p.Name).ShouldBe(
            ["format", "key", "statistics", "symbolMapEntries", "skippedItems", "warnings"]);
        document.RootElement.GetProperty("key").GetString()!.Length.ShouldBe(64);
        document.RootElement.GetProperty("statistics").EnumerateObject()
            .ShouldAllBe(p => p.Value.ValueKind == JsonValueKind.Number);
        document.RootElement.GetProperty("symbolMapEntries").GetInt32().ShouldBe(first.SymbolMap.Count);
    }

    [Fact]
    public async Task CacheFileInTheOldFormat_IsAMiss_AndTheRerunWritesTheNewFormat()
    {
        using var container = CoreServicesTestSupport.BuildContainer();
        var service = container.Resolve<IObfuscationService>();
        var input = CompileDistinctive();
        var output = Path.Combine(_dir, "Lib.obfuscated.dll");
        var settings = RenamingIncremental();
        await service.ObfuscateAsync(input, output, settings);

        // Exactly what earlier builds wrote: the bare hex key, valid for this input and these settings.
        var key = IncrementalCache.ComputeKey(input, settings);
        await File.WriteAllTextAsync(IncrementalCache.CachePath(output), key);
        IncrementalCache.TryHit(input, output, settings).ShouldBeFalse("a bare key has no numbers to return");
        var rerun = await service.ObfuscateAsync(input, output, settings);

        rerun.Success.ShouldBeTrue(rerun.ErrorMessage);
        rerun.FromCache.ShouldBeFalse();
        rerun.Statistics.TotalTransformations.ShouldBeGreaterThan(0);
        (await service.ObfuscateAsync(input, output, settings)).FromCache.ShouldBeTrue("the rerun rewrote the cache");
        using var document = JsonDocument.Parse(await File.ReadAllBytesAsync(IncrementalCache.CachePath(output)));
        document.RootElement.GetProperty("format").GetInt32().ShouldBe(2);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{ this is not json")]
    [InlineData("{\"format\":2,\"key\":\"ABC")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("42")]
    [InlineData("{\"format\":2}")]
    [InlineData("{\"format\":1,\"key\":\"KEY\",\"statistics\":{}}")]
    [InlineData("{\"format\":2,\"key\":\"KEY\",\"statistics\":null,\"symbolMapEntries\":0,\"skippedItems\":0,\"warnings\":0}")]
    [InlineData("{\"format\":2,\"key\":\"KEY\",\"statistics\":{},\"symbolMapEntries\":-1,\"skippedItems\":0,\"warnings\":0}")]
    [InlineData("{\"format\":2,\"key\":\"KEY\",\"statistics\":{\"stringsEncrypted\":\"many\"},\"symbolMapEntries\":0,\"skippedItems\":0,\"warnings\":0}")]
    [InlineData("{\"format\":2,\"key\":\"0000000000000000000000000000000000000000000000000000000000000000\",\"statistics\":{},\"symbolMapEntries\":0,\"skippedItems\":0,\"warnings\":0}")]
    public async Task CorruptOrForeignCacheFile_IsAMiss_NeverAnException(string content)
    {
        using var container = CoreServicesTestSupport.BuildContainer();
        var service = container.Resolve<IObfuscationService>();
        var input = CompileDistinctive();
        var output = Path.Combine(_dir, "Lib.obfuscated.dll");
        var settings = RenamingIncremental();
        await service.ObfuscateAsync(input, output, settings);
        var key = IncrementalCache.ComputeKey(input, settings);
        await File.WriteAllTextAsync(IncrementalCache.CachePath(output), content.Replace("KEY", key));

        IncrementalCache.TryHit(input, output, settings).ShouldBeFalse();
        var rerun = await service.ObfuscateAsync(input, output, settings);

        rerun.Success.ShouldBeTrue(rerun.ErrorMessage);
        rerun.FromCache.ShouldBeFalse();
        rerun.Statistics.TotalTransformations.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Hit_SaysHowManyItemsWereSkippedAndWarningsRaised_WithoutNamingThem()
    {
        using var container = CoreServicesTestSupport.BuildContainer();
        var service = container.Resolve<IObfuscationService>();
        // Virtualization skips a method that has an exception handler and names it in a skipped item and in the
        // maxMethods warning. Those are exactly the texts that must not reach the cache file.
        var input = CoreServicesTestSupport.CompileLibrary(_dir, "Lib",
            "public class Gizmo { " +
            "public int UnicornTryMethod(int x) { try { return x / (x - 1); } catch (System.DivideByZeroException) { return -1; } } " +
            "public int PlainOne(int x) => x * 3 + 1; " +
            "public int PlainTwo(int x) => x * 5 + 2; }");
        var output = Path.Combine(_dir, "Lib.obfuscated.dll");
        var settings = Incremental();
        settings.Virtualization.Enabled = true;
        settings.Virtualization.MaxMethods = 1;

        var first = await service.ObfuscateAsync(input, output, settings);
        var second = await service.ObfuscateAsync(input, output, settings);

        first.Success.ShouldBeTrue(first.ErrorMessage);
        first.SkippedItems.ShouldContain(i => i.ItemName.Contains("UnicornTryMethod", StringComparison.Ordinal),
            "the sample must produce a skipped item that carries an original name");
        first.Warnings.ShouldContain(w => w.StartsWith("Virtualization: maxMethods=", StringComparison.Ordinal));
        second.FromCache.ShouldBeTrue();
        second.SkippedItems.ShouldBeEmpty();
        second.Warnings.ShouldContain(w =>
            w.StartsWith($"The run that produced this output skipped {first.SkippedItems.Count} item(s)", StringComparison.Ordinal));
        second.Warnings.ShouldContain(w =>
            w.StartsWith("The run that produced this output raised 1 warning(s)", StringComparison.Ordinal));
        second.Warnings.ShouldNotContain(w => w.Contains("UnicornTryMethod", StringComparison.Ordinal));
        var bytes = await File.ReadAllBytesAsync(IncrementalCache.CachePath(output));
        ContainsBytes(bytes, Encoding.UTF8.GetBytes("UnicornTryMethod")).ShouldBeFalse();
        ContainsBytes(bytes, Encoding.UTF8.GetBytes("Gizmo")).ShouldBeFalse();
        ContainsBytes(bytes, Encoding.UTF8.GetBytes("PlainOne")).ShouldBeFalse();
    }

    [Fact]
    public async Task MergeRun_UsesTheSameRules_WhetherOrNotTheCallerNeedsTheSymbolMap()
    {
        using var container = CoreServicesTestSupport.BuildContainer();
        var service = container.Resolve<IObfuscationService>();
        var libA = CoreServicesTestSupport.CompileLibrary(_dir, "LibA", "public class AlphaWidget { public static int N() => 1; }");
        var libB = CoreServicesTestSupport.CompileLibrary(_dir, "LibB", "public class BetaWidget { public static int N() => 2; }");
        var output = Path.Combine(_dir, "Merged.dll");
        var settings = RenamingIncremental();

        var first = await service.MergeAndObfuscateAsync([libA, libB], output, settings);
        var second = await service.MergeAndObfuscateAsync([libA, libB], output, settings, NeedsMap);
        var third = await service.MergeAndObfuscateAsync([libA, libB], output, settings);

        first.Success.ShouldBeTrue(first.ErrorMessage);
        first.SymbolMap.ShouldNotBeEmpty();
        second.Success.ShouldBeTrue(second.ErrorMessage);
        second.FromCache.ShouldBeFalse("the caller needs the map and a hit has none");
        second.SymbolMap.ShouldNotBeEmpty();
        third.Success.ShouldBeTrue(third.ErrorMessage);
        third.FromCache.ShouldBeTrue("the merge is deterministic, so the run that rewrote the cache can be reused");
        third.Statistics.TotalTransformations.ShouldBe(second.Statistics.TotalTransformations);
    }

    private static bool ContainsBytes(byte[] haystack, byte[] needle) =>
        haystack.AsSpan().IndexOf(needle) >= 0;

    [Theory]
    [InlineData("..\\outside\\Lib")]
    [InlineData("../outside/Lib")]
    public void ComputeKey_AssemblyRefNameWithTraversal_DoesNotReadOutsideTheInputDirectory(string refName)
    {
        var inputDir = Path.Combine(_dir, "in");
        var outsideDir = Path.Combine(_dir, "outside");
        Directory.CreateDirectory(inputDir);
        Directory.CreateDirectory(outsideDir);
        var input = Path.Combine(inputDir, "App.dll");
        WriteModuleWithAssemblyRef(input, "App", refName);
        var outside = Path.Combine(outsideDir, "Lib.dll");
        File.WriteAllBytes(outside, [1, 2, 3]);
        var settings = CoreServicesTestSupport.AllOff();
        settings.DependencyEmbedding.Enabled = true;

        var first = IncrementalCache.ComputeKey(input, settings);
        File.WriteAllBytes(outside, [9, 9, 9]);

        IncrementalCache.ComputeKey(input, settings).ShouldBe(first);
    }

    [Fact]
    public void ComputeKey_AssemblyRefNameThatIsAnAbsolutePath_DoesNotReadThatFile()
    {
        var input = Path.Combine(_dir, "App.dll");
        var target = Path.Combine(_dir, "elsewhere", "Lib");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        WriteModuleWithAssemblyRef(input, "App", target);
        File.WriteAllBytes(target + ".dll", [1, 2, 3]);
        var settings = CoreServicesTestSupport.AllOff();
        settings.DependencyEmbedding.Enabled = true;

        var first = IncrementalCache.ComputeKey(input, settings);
        File.WriteAllBytes(target + ".dll", [9, 9, 9]);

        IncrementalCache.ComputeKey(input, settings).ShouldBe(first);
    }

    [Fact]
    public void ComputeKey_PlainSiblingDependency_StillCountsTowardTheKey()
    {
        var input = Path.Combine(_dir, "App.dll");
        WriteModuleWithAssemblyRef(input, "App", "Lib");
        var dep = Path.Combine(_dir, "Lib.dll");
        File.WriteAllBytes(dep, [1, 2, 3]);
        var settings = CoreServicesTestSupport.AllOff();
        settings.DependencyEmbedding.Enabled = true;

        var first = IncrementalCache.ComputeKey(input, settings);
        File.WriteAllBytes(dep, [1, 2, 4]);

        IncrementalCache.ComputeKey(input, settings).ShouldNotBe(first);
    }

    private static ObfuscationService NewService(IContainer container, IAssemblyProcessor processor) =>
        new(
            processor,
            container.Resolve<ISourceProcessor>(),
            container.Resolve<IObfuscationPipeline>(),
            container.Resolve<IAssemblyMerger>(),
            NullLogger<ObfuscationService>.Instance,
            container.Resolve<IClosedSetProcessor>());

    private static void WriteModuleWithAssemblyRef(string path, string assemblyName, string referencedName)
    {
        var module = new ModuleDefUser(assemblyName + ".dll", Guid.NewGuid(), AssemblyRefUser.CreateMscorlibReferenceCLR40());
        var assembly = new AssemblyDefUser(assemblyName, new Version(1, 0, 0, 0));
        assembly.Modules.Add(module);
        var type = new TypeDefUser("App", "Program", module.CorLibTypes.Object.TypeDefOrRef)
        {
            Attributes = TypeAttributes.Public | TypeAttributes.Class
        };
        module.Types.Add(type);
        var libRef = new AssemblyRefUser(referencedName, new Version(1, 0, 0, 0));
        var libType = new TypeRefUser(module, "LibNs", "Lib", libRef);
        var callee = new MemberRefUser(module, "N", MethodSig.CreateStatic(module.CorLibTypes.Int32), libType);
        var method = new MethodDefUser(
            "Go",
            MethodSig.CreateStatic(module.CorLibTypes.Int32),
            MethodImplAttributes.IL,
            MethodAttributes.Public | MethodAttributes.Static)
        {
            Body = new CilBody()
        };
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Call, callee));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        type.Methods.Add(method);
        module.Write(path);
    }
}
