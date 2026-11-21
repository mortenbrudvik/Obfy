using Obfy.Core.Models;
using Obfy.Core.Utilities;
using Shouldly;

namespace Obfy.Tests;

/// <summary>
/// The cache entry is written through a temporary file beside it and moved into place, the way
/// <c>AssemblyProcessor.SaveAsync</c> writes outputs. Two runs on the same output, or a crash in the middle of the write,
/// then never leave a truncated or mismatched entry, and an entry that was valid stays valid when the next write fails.
/// A failure to write the cache is still only <c>false</c> (a warning for the caller), never an exception.
/// </summary>
public class IncrementalCacheAtomicWriteTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "obfy-cache-atomic-" + Guid.NewGuid().ToString("N"));
    private readonly ObfySettings _settings = new() { Level = ObfuscationLevel.Custom };
    private readonly string _output;

    public IncrementalCacheAtomicWriteTests()
    {
        Directory.CreateDirectory(_dir);
        _output = Path.Combine(_dir, "out.dll");
        File.WriteAllBytes(_output, [5, 6, 7, 8]);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* ignore */ }
        GC.SuppressFinalize(this);
    }

    private static string Key(char digit) => new(digit, 64);

    private bool Write(string key, Action<string, string>? writeText = null) =>
        IncrementalCache.TryWrite(_output, key, CachedRun.Empty, _settings.Packing, writeText);

    private bool IsHitFor(string key) => IncrementalCache.TryReadHit(_output, _settings, key) is not null;

    private void OnlyTheOutputAndItsCacheAreHere() =>
        Directory.GetFileSystemEntries(_dir).Select(Path.GetFileName).Order().ShouldBe(["out.dll", "out.dll.obfycache"]);

    /// <summary>A write that goes wrong halfway: some of the text reaches the file, then the disk gives up.</summary>
    private static void DiesHalfway(string path, string text)
    {
        File.WriteAllText(path, text[..(text.Length / 2)]);
        throw new IOException("The disk is full.");
    }

    [Fact]
    public void TryWrite_HandsTheTextToAFileBesideTheCache_NotToTheCacheItself()
    {
        string? given = null;

        Write(Key('a'), (path, text) =>
        {
            given = path;
            File.WriteAllText(path, text);
        }).ShouldBeTrue();

        var cache = IncrementalCache.CachePath(_output);
        given.ShouldNotBeNull();
        given.ShouldNotBe(cache, "an entry written in place can be truncated by a crash or read half-written by a second run");
        Path.GetDirectoryName(given).ShouldBe(Path.GetDirectoryName(cache), "the same folder, so the move is a rename");
        given.ShouldEndWith(".tmp");
        IsHitFor(Key('a')).ShouldBeTrue();
        OnlyTheOutputAndItsCacheAreHere();
    }

    [Fact]
    public void TryWrite_ThatDiesHalfway_LeavesAnExistingValidEntryAsItWas()
    {
        Write(Key('a')).ShouldBeTrue();
        var before = File.ReadAllText(IncrementalCache.CachePath(_output));

        Write(Key('b'), DiesHalfway).ShouldBeFalse("a failed write is reported as false, never thrown");

        File.ReadAllText(IncrementalCache.CachePath(_output)).ShouldBe(before);
        IsHitFor(Key('a')).ShouldBeTrue("the earlier entry is still valid");
        IsHitFor(Key('b')).ShouldBeFalse();
        OnlyTheOutputAndItsCacheAreHere();
    }

    [Fact]
    public void TryWrite_ThatDiesHalfway_WithNoEntryYet_LeavesNoCacheFileAndNoTempFile()
    {
        Write(Key('a'), DiesHalfway).ShouldBeFalse();

        File.Exists(IncrementalCache.CachePath(_output)).ShouldBeFalse("no partial entry");
        Directory.GetFileSystemEntries(_dir).Select(Path.GetFileName).ShouldBe(["out.dll"]);
    }

    [Fact]
    public void TryWrite_ReplacesAnEarlierEntry_AndLeavesNoTempFile()
    {
        Write(Key('a')).ShouldBeTrue();
        Write(Key('b')).ShouldBeTrue();

        IsHitFor(Key('b')).ShouldBeTrue();
        IsHitFor(Key('a')).ShouldBeFalse();
        OnlyTheOutputAndItsCacheAreHere();
    }

    [Fact]
    public void TryWrite_WhenTheCacheFilesNameIsTakenByAFolder_IsFalse_AndNoTempFileIsLeft()
    {
        var cache = IncrementalCache.CachePath(_output);
        Directory.CreateDirectory(cache);

        Write(Key('a')).ShouldBeFalse();

        Directory.Exists(cache).ShouldBeTrue("the folder was not touched");
        Directory.GetFileSystemEntries(_dir).Select(Path.GetFileName).Order().ShouldBe(["out.dll", "out.dll.obfycache"]);
        Directory.GetFiles(_dir, "*.tmp").ShouldBeEmpty();
    }

    [Fact]
    public void TryWrite_WhenTheTempFileCannotBeWritten_IsFalse()
    {
        Write(Key('a'), static (_, _) => throw new UnauthorizedAccessException("denied")).ShouldBeFalse();

        File.Exists(IncrementalCache.CachePath(_output)).ShouldBeFalse();
        Directory.GetFileSystemEntries(_dir).Select(Path.GetFileName).ShouldBe(["out.dll"]);
    }
}
