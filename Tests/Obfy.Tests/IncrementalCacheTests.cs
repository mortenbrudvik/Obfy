using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
