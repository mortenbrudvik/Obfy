using Obfy.VisualStudio.Services;
using Shouldly;

namespace Obfy.VisualStudio.Tests;

public class PostBuildStampTests : IDisposable
{
    private readonly string _projectDir = Path.Combine(Path.GetTempPath(), "obfy-stamp-" + Guid.NewGuid().ToString("N"));
    private readonly string _binDir;
    private readonly string _assembly;

    public PostBuildStampTests()
    {
        _binDir = Path.Combine(_projectDir, "bin", "Release", "net10.0");
        Directory.CreateDirectory(_binDir);
        _assembly = Path.Combine(_binDir, "App.dll");
        File.WriteAllText(_assembly, "freshly built");
    }

    [Fact]
    public void FreshlyBuiltAssembly_IsNotAlreadyObfuscated()
    {
        PostBuildStamp.IsAlreadyObfuscated(_projectDir, _assembly).ShouldBeFalse();
    }

    [Fact]
    public void AfterRecording_AnUntouchedAssembly_IsSkipped()
    {
        File.WriteAllText(_assembly, "obfuscated output");
        PostBuildStamp.Record(_projectDir, _assembly);

        // A Debug build, or an up-to-date project, leaves the Release output exactly as it was.
        PostBuildStamp.IsAlreadyObfuscated(_projectDir, _assembly).ShouldBeTrue();
    }

    [Fact]
    public void AfterRecording_ARebuiltAssembly_IsObfuscatedAgain()
    {
        File.WriteAllText(_assembly, "obfuscated output");
        PostBuildStamp.Record(_projectDir, _assembly);

        File.WriteAllText(_assembly, "a different, rebuilt assembly");

        PostBuildStamp.IsAlreadyObfuscated(_projectDir, _assembly).ShouldBeFalse();
    }

    [Fact]
    public void AfterRecording_ARecopiedAssemblyOfTheSameSize_IsObfuscatedAgain()
    {
        File.WriteAllText(_assembly, "obfuscated output");
        PostBuildStamp.Record(_projectDir, _assembly);

        // MSBuild copies obj\ over bin\ and keeps the source timestamp, so only the timestamp may differ.
        File.SetLastWriteTimeUtc(_assembly, File.GetLastWriteTimeUtc(_assembly).AddMinutes(-5));

        PostBuildStamp.IsAlreadyObfuscated(_projectDir, _assembly).ShouldBeFalse();
    }

    [Fact]
    public void Stamps_AreKeptPerAssembly()
    {
        var other = Path.Combine(_binDir, "Other.dll");
        File.WriteAllText(other, "other");
        File.WriteAllText(_assembly, "obfuscated output");
        PostBuildStamp.Record(_projectDir, _assembly);

        PostBuildStamp.IsAlreadyObfuscated(_projectDir, other).ShouldBeFalse();
    }

    [Fact]
    public void Stamps_AreNotWrittenNextToTheAssembly()
    {
        PostBuildStamp.Record(_projectDir, _assembly);

        Directory.GetFiles(_binDir).Select(Path.GetFileName).ShouldBe(new[] { "App.dll" });
        Directory.Exists(Path.Combine(_projectDir, "obj")).ShouldBeTrue();
    }

    [Fact]
    public void StampFormat_IsSharedWithTheRiderPlugin()
    {
        File.WriteAllText(_assembly, "obfuscated output");
        PostBuildStamp.Record(_projectDir, _assembly);

        // obj\obfy-postbuild\<name>-<16 hex>.stamp containing "<length>:<unix milliseconds>"; the Rider plugin
        // writes and reads the same, so a project built from both IDEs is never obfuscated twice.
        var stamp = Directory.GetFiles(Path.Combine(_projectDir, "obj", "obfy-postbuild")).Single();
        Path.GetFileName(stamp).ShouldMatch("^App-[0-9a-f]{16}\\.stamp$");

        // The 16 hex digits are the first 8 bytes of SHA-256 over the upper-cased full assembly path.
        var hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(_assembly).ToUpperInvariant()));
        Path.GetFileName(stamp).ShouldBe($"App-{Convert.ToHexString(hash, 0, 8).ToLowerInvariant()}.stamp");

        var info = new FileInfo(_assembly);
        var expected = $"{info.Length}:{new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeMilliseconds()}";
        File.ReadAllText(stamp).ShouldBe(expected);
    }

    [Fact]
    public void MissingAssembly_IsNeverSkippedAndCanBeRecordedWithoutThrowing()
    {
        var missing = Path.Combine(_binDir, "Gone.dll");

        Should.NotThrow(() => PostBuildStamp.Record(_projectDir, missing));
        PostBuildStamp.IsAlreadyObfuscated(_projectDir, missing).ShouldBeFalse();
    }

    public void Dispose()
    {
        if (Directory.Exists(_projectDir))
            Directory.Delete(_projectDir, recursive: true);
    }
}
