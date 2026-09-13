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
    public void TryHit_PackingEnabledWithoutLauncher_IsFalse()
    {
        var (input, output, settings) = Seed();
        IncrementalCache.Write(input, output, settings);
        settings.Packing.Enabled = true;
        IncrementalCache.TryHit(input, output, settings).ShouldBeFalse();
    }

    [Fact]
    public void TryHit_CacheWrittenWithoutProductVersion_IsFalse()
    {
        var (input, output, settings) = Seed();
        IncrementalCache.Write(input, output, settings);
        File.ReadAllText(IncrementalCache.CachePath(output)).Trim().Length.ShouldBe(64);
        File.WriteAllText(IncrementalCache.CachePath(output), "deadbeef");
        IncrementalCache.TryHit(input, output, settings).ShouldBeFalse();
    }

    [Fact]
    public void TryHit_CorruptCacheFile_IsFalse()
    {
        var (input, output, settings) = Seed();
        File.WriteAllText(IncrementalCache.CachePath(output), "");
        IncrementalCache.TryHit(input, output, settings).ShouldBeFalse();
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
}
