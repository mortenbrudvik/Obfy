using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using Obfy.Core.Models;
using Obfy.Core.Utilities;
using Shouldly;

namespace Obfy.Tests;

public class IncrementalCacheTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "obfy-cache-" + Guid.NewGuid().ToString("N"));

    public IncrementalCacheTests() => Directory.CreateDirectory(_dir);

    [Fact]
    public void TryHit_SameInputAndSettings_IsTrue()
    {
        var (input, output, settings) = Seed();
        IncrementalCache.Write(input, output, settings);
        IncrementalCache.TryHit(input, output, settings).ShouldBeTrue();
    }

    [Fact]
    public void TryHit_SettingsChange_IsFalse()
    {
        var (input, output, settings) = Seed();
        IncrementalCache.Write(input, output, settings);
        settings.StringEncryption.Enabled = !settings.StringEncryption.Enabled;
        IncrementalCache.TryHit(input, output, settings).ShouldBeFalse();
    }

    [Fact]
    public void TryHit_InputBytesChange_IsFalse()
    {
        var (input, output, settings) = Seed();
        IncrementalCache.Write(input, output, settings);
        File.WriteAllBytes(input, [9, 9, 9]);
        IncrementalCache.TryHit(input, output, settings).ShouldBeFalse();
    }

    [Fact]
    public void TryHit_MissingOutput_IsFalse()
    {
        var (input, output, settings) = Seed();
        IncrementalCache.Write(input, output, settings);
        File.Delete(output);
        IncrementalCache.TryHit(input, output, settings).ShouldBeFalse();
    }

    [Fact]
    public void TryHit_PackingWinX64WithoutRuntimeConfig_IsFalse()
    {
        var (input, output, settings) = Seed();
        settings.Packing.Enabled = true;
        IncrementalCache.Write(input, output, settings);
        IncrementalCache.TryHit(input, output, settings).ShouldBeFalse();
    }

    [Fact]
    public void TryHit_PackingWinX64DummyOutputWithRuntimeConfig_IsFalse()
    {
        var (input, output, settings) = Seed();
        settings.Packing.Enabled = true;
        File.WriteAllText(NativePacker.RuntimeConfigPathFor(output), "{}");
        IncrementalCache.TryWrite(input, output, settings).ShouldBeTrue();
        IncrementalCache.TryHit(input, output, settings).ShouldBeFalse("the output is not a packed host");
    }

    [Fact]
    public void TryHit_PackingWinX64WithRuntimeConfigAndPackedHost_IsTrue()
    {
        var (input, output, settings) = Seed();
        settings.Packing.Enabled = true;
        var packed = new byte[8];
        packed[0] = (byte)'M';
        packed[1] = (byte)'Z';
        "OBP1"u8.CopyTo(packed.AsSpan(4));
        File.WriteAllBytes(output, packed);
        File.WriteAllText(NativePacker.RuntimeConfigPathFor(output), "{}");
        // The entry fingerprints the files as they are when it is written, so they come first.
        IncrementalCache.TryWrite(input, output, settings).ShouldBeTrue();
        IncrementalCache.TryHit(input, output, settings).ShouldBeTrue();
    }

    [Fact]
    public void ComputeKey_ChangesWhenPackingRidChanges()
    {
        var (input, _, settings) = Seed();
        settings.Packing.Enabled = true;
        settings.Packing.Rid = "win-x64";
        var native = IncrementalCache.ComputeKey(input, settings);
        settings.Packing.Rid = "portable";
        IncrementalCache.ComputeKey(input, settings).ShouldNotBe(native);
    }

    [Fact]
    public void TryHit_PackingPortableWithoutLauncher_IsFalse()
    {
        var (input, output, settings) = Seed();
        settings.Packing.Enabled = true;
        settings.Packing.Rid = "portable";
        IncrementalCache.Write(input, output, settings);
        IncrementalCache.TryHit(input, output, settings).ShouldBeFalse();
    }

    [Fact]
    public void TryHit_CacheWrittenWithoutProductVersion_IsFalse()
    {
        var (input, output, settings) = Seed();
        IncrementalCache.Write(input, output, settings);
        using (var entry = JsonDocument.Parse(File.ReadAllText(IncrementalCache.CachePath(output))))
            entry.RootElement.GetProperty("key").GetString()!.Length.ShouldBe(64);
        File.WriteAllText(IncrementalCache.CachePath(output), "deadbeef");
        IncrementalCache.TryHit(input, output, settings).ShouldBeFalse();
    }

    [Fact]
    public void TryHit_CacheKeyWithoutVersionPrefix_IsFalse()
    {
        var (input, output, settings) = Seed();
        var inputBytes = File.ReadAllBytes(input);
        var jsonBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(settings, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        }));
        var payload = new byte[inputBytes.Length + jsonBytes.Length];
        Buffer.BlockCopy(inputBytes, 0, payload, 0, inputBytes.Length);
        Buffer.BlockCopy(jsonBytes, 0, payload, inputBytes.Length, jsonBytes.Length);
        File.WriteAllText(IncrementalCache.CachePath(output), Convert.ToHexString(SHA256.HashData(payload)));

        IncrementalCache.TryHit(input, output, settings).ShouldBeFalse();
    }

    [Fact]
    public void ProductVersion_IsStampedOnCoreAssembly()
    {
        var version = typeof(IncrementalCache).Assembly.GetName().Version;
        version.ShouldNotBeNull();
        version.ShouldNotBe(new Version(0, 0, 0, 0));
    }

    [Fact]
    public void TryHit_LockedCacheFile_IsFalse()
    {
        var (input, output, settings) = Seed();
        IncrementalCache.Write(input, output, settings);
        using var _ = new FileStream(
            IncrementalCache.CachePath(output), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        IncrementalCache.TryHit(input, output, settings).ShouldBeFalse();
    }

    [Fact]
    public void TryWrite_LockedCacheFile_ReturnsFalse()
    {
        var (input, output, settings) = Seed();
        IncrementalCache.Write(input, output, settings);
        using var _ = new FileStream(
            IncrementalCache.CachePath(output), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        IncrementalCache.TryWrite(input, output, settings).ShouldBeFalse();
    }

    [Fact]
    public void ComputeKey_ChangesWhenSigningKeyBytesChange()
    {
        var (input, _, settings) = Seed();
        var keyFile = Path.Combine(_dir, "app.snk");
        File.WriteAllBytes(keyFile, [1, 2, 3]);
        settings.Signing.Enabled = true;
        settings.Signing.KeyFile = keyFile;

        var first = IncrementalCache.ComputeKey(input, settings);
        File.WriteAllBytes(keyFile, [9, 9, 9]);

        IncrementalCache.ComputeKey(input, settings).ShouldNotBe(first);
    }

    [Fact]
    public void ComputeKey_ChangesWhenEmbeddedDependencyBytesChange()
    {
        var input = Path.Combine(_dir, "App.dll");
        var dep = Path.Combine(_dir, "Lib.dll");
        WriteModuleWithAssemblyRef(input, "App", "Lib");
        File.WriteAllBytes(dep, [1, 2, 3]);
        var settings = new ObfySettings
        {
            Level = ObfuscationLevel.Custom,
            DependencyEmbedding = { Enabled = true }
        };

        var first = IncrementalCache.ComputeKey(input, settings);
        File.WriteAllBytes(dep, [1, 2, 4]);

        IncrementalCache.ComputeKey(input, settings).ShouldNotBe(first);
    }

    [Fact]
    public void ComputeKey_IgnoresSiblingBytesWhenEmbeddingIsOff()
    {
        var input = Path.Combine(_dir, "App.dll");
        var dep = Path.Combine(_dir, "Lib.dll");
        WriteModuleWithAssemblyRef(input, "App", "Lib");
        File.WriteAllBytes(dep, [1, 2, 3]);
        var settings = new ObfySettings { Level = ObfuscationLevel.Custom };

        var first = IncrementalCache.ComputeKey(input, settings);
        File.WriteAllBytes(dep, [1, 2, 4]);

        IncrementalCache.ComputeKey(input, settings).ShouldBe(first);
    }

    [Fact]
    public void ComputeKey_IgnoresExcludedEmbeddedDependency()
    {
        var input = Path.Combine(_dir, "App.dll");
        var dep = Path.Combine(_dir, "Lib.dll");
        WriteModuleWithAssemblyRef(input, "App", "Lib");
        File.WriteAllBytes(dep, [1, 2, 3]);
        var settings = new ObfySettings
        {
            Level = ObfuscationLevel.Custom,
            DependencyEmbedding =
            {
                Enabled = true,
                ExcludePatterns = { "Lib.dll" }
            }
        };

        var first = IncrementalCache.ComputeKey(input, settings);
        File.WriteAllBytes(dep, [1, 2, 4]);

        IncrementalCache.ComputeKey(input, settings).ShouldBe(first);
    }

    [Fact]
    public void TryHit_CorruptCacheFile_IsFalse()
    {
        var (input, output, settings) = Seed();
        File.WriteAllText(IncrementalCache.CachePath(output), "");
        IncrementalCache.TryHit(input, output, settings).ShouldBeFalse();
    }

    [Fact]
    public void TryReadHit_ReturnsTheRunRecordThatWasWritten()
    {
        var (input, output, settings) = Seed();
        var key = IncrementalCache.ComputeKey(input, settings);
        var run = new CachedRun(
            new ObfuscationStatistics { StringsEncrypted = 7, TypesRenamed = 3, MethodsRenamed = 5, AssembliesEmbedded = 1 },
            SymbolMapEntries: 11, SkippedItems: 2, Warnings: 4);

        IncrementalCache.TryWrite(output, key, run, settings.Packing).ShouldBeTrue();
        var read = IncrementalCache.TryReadHit(output, settings, key);

        read.ShouldNotBeNull();
        read.SymbolMapEntries.ShouldBe(11);
        read.SkippedItems.ShouldBe(2);
        read.Warnings.ShouldBe(4);
        read.Statistics.StringsEncrypted.ShouldBe(7);
        read.Statistics.TypesRenamed.ShouldBe(3);
        read.Statistics.MethodsRenamed.ShouldBe(5);
        read.Statistics.AssembliesEmbedded.ShouldBe(1);
        read.Statistics.TotalTransformations.ShouldBe(16);
    }

    [Fact]
    public void TryHit_BareKeyFromTheOldFormat_IsAMiss()
    {
        // Earlier builds stored only the hex key. It would still match, but there are no numbers to give back, and
        // a hit with zero statistics is the defect this format replaced.
        var (input, output, settings) = Seed();
        File.WriteAllText(IncrementalCache.CachePath(output), IncrementalCache.ComputeKey(input, settings));

        IncrementalCache.TryHit(input, output, settings).ShouldBeFalse();
        IncrementalCache.TryReadHit(output, settings, IncrementalCache.ComputeKey(input, settings)).ShouldBeNull();
    }

    [Fact]
    public void TryHit_OutputChangedAfterTheCacheWasWritten_IsFalse()
    {
        var (input, output, settings) = Seed();
        IncrementalCache.TryWrite(input, output, settings).ShouldBeTrue();
        IncrementalCache.TryHit(input, output, settings).ShouldBeTrue();

        File.WriteAllBytes(output, [5, 6, 7, 9]);

        IncrementalCache.TryHit(input, output, settings).ShouldBeFalse("same length, one byte different");
    }

    [Fact]
    public void TryWrite_OutputMissing_WritesNothing()
    {
        var (input, output, settings) = Seed();
        File.Delete(output);

        IncrementalCache.TryWrite(input, output, settings).ShouldBeFalse("there is no file to vouch for");
        File.Exists(IncrementalCache.CachePath(output)).ShouldBeFalse();
    }

    [Fact]
    public void TryWrite_PackingOnButTheSidecarIsMissing_WritesNothing()
    {
        var (input, output, settings) = Seed();
        settings.Packing.Enabled = true;

        IncrementalCache.TryWrite(input, output, settings).ShouldBeFalse();
        File.Exists(IncrementalCache.CachePath(output)).ShouldBeFalse();
    }

    [Fact]
    public void TryHit_OutputHeldOpenForWritingByAnotherProcessWithoutChange_IsStillAHit()
    {
        var (input, output, settings) = Seed();
        IncrementalCache.TryWrite(input, output, settings).ShouldBeTrue();

        // An indexer, a scanner or an editor holding the file open for writing has not changed it. Reading it to
        // check the fingerprint must not fail on that, or the hit turns into a rebuild that cannot save.
        using var holder = new FileStream(output, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);

        IncrementalCache.TryHit(input, output, settings).ShouldBeTrue();
    }

    [Fact]
    public void TryHit_OutputLockedExclusively_IsAMissNotAnException()
    {
        var (input, output, settings) = Seed();
        IncrementalCache.TryWrite(input, output, settings).ShouldBeTrue();

        using var holder = new FileStream(output, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        Should.NotThrow(() => IncrementalCache.TryHit(input, output, settings)).ShouldBeFalse();
    }

    [Fact]
    public void TryReadHit_KeyDiffersInCase_StillMatches()
    {
        var (input, output, settings) = Seed();
        var key = IncrementalCache.ComputeKey(input, settings);
        IncrementalCache.TryWrite(output, key.ToLowerInvariant(), CachedRun.Empty, settings.Packing).ShouldBeTrue();

        IncrementalCache.TryReadHit(output, settings, key).ShouldNotBeNull();
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* ignore */ }
        GC.SuppressFinalize(this);
    }

    private (string Input, string Output, ObfySettings Settings) Seed()
    {
        var input = Path.Combine(_dir, "in.dll");
        var output = Path.Combine(_dir, "out.dll");
        File.WriteAllBytes(input, [1, 2, 3, 4]);
        File.WriteAllBytes(output, [5, 6, 7, 8]);
        var settings = new ObfySettings { Level = ObfuscationLevel.Custom };
        return (input, output, settings);
    }

    private static void WriteModuleWithAssemblyRef(string path, string assemblyName, string referencedName)
    {
        var module = new ModuleDefUser(
            assemblyName + ".dll",
            Guid.NewGuid(),
            AssemblyRefUser.CreateMscorlibReferenceCLR40());
        var assembly = new AssemblyDefUser(assemblyName, new Version(1, 0, 0, 0));
        assembly.Modules.Add(module);
        var type = new TypeDefUser("App", "Program", module.CorLibTypes.Object.TypeDefOrRef)
        {
            Attributes = TypeAttributes.Public | TypeAttributes.Class
        };
        module.Types.Add(type);
        var libRef = new AssemblyRefUser(referencedName, new Version(1, 0, 0, 0));
        var libType = new TypeRefUser(module, "LibNs", "Lib", libRef);
        var callee = new MemberRefUser(
            module,
            "N",
            MethodSig.CreateStatic(module.CorLibTypes.Int32),
            libType);
        var method = new MethodDefUser(
            "Go",
            MethodSig.CreateStatic(module.CorLibTypes.Int32),
            MethodImplAttributes.IL,
            MethodAttributes.Public | MethodAttributes.Static);
        method.Body = new CilBody();
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Call, callee));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        type.Methods.Add(method);
        module.Write(path);
    }
}
