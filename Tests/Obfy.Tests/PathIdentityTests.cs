using Obfy.App.Tests.Common;
using Obfy.Core.Utilities;
using Shouldly;

namespace Obfy.Tests;

/// <summary>
/// <see cref="PathIdentity"/> decides whether a run's output replaces its input. The fingerprint of the output is the
/// guard that keeps a stale cache from vouching for an unprotected file; this makes the "output replaces the input"
/// note appear for every spelling of the same file.
/// </summary>
public class PathIdentityTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"obfy-path-identity-{Guid.NewGuid():N}");

    public PathIdentityTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            // A junction is removed on its own first: a recursive delete must not follow it into its target.
            foreach (var dir in Directory.GetDirectories(_root))
            {
                if (new DirectoryInfo(dir).Attributes.HasFlag(FileAttributes.ReparsePoint))
                    Directory.Delete(dir);
            }

            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }

    private string Make(string relative)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");
        return path;
    }

    [Fact]
    public void TheSamePath_IsTheSameFile() =>
        PathIdentity.SameFile(Make("a.dll"), Path.Combine(_root, "a.dll")).ShouldBeTrue();

    [Fact]
    public void ADotDotDetour_IsTheSameFile()
    {
        var file = Make(Path.Combine("sub", "a.dll"));
        Directory.CreateDirectory(Path.Combine(_root, "other"));

        PathIdentity.SameFile(file, Path.Combine(_root, "other", "..", "sub", "a.dll")).ShouldBeTrue();
    }

    [Fact]
    public void ARelativePath_IsResolvedAgainstTheCurrentDirectory()
    {
        var file = Make("a.dll");

        PathIdentity.SameFile(file, Path.GetRelativePath(Directory.GetCurrentDirectory(), file)).ShouldBeTrue();
    }

    [Fact]
    public void ADifferentCase_IsTheSameFileOnWindowsOnly()
    {
        var file = Make("a.dll");
        var upper = Path.Combine(_root.ToUpperInvariant(), "A.DLL");

        PathIdentity.SameFile(file, upper).ShouldBe(OperatingSystem.IsWindows());
    }

    [Fact]
    public void TwoDifferentFiles_AreNotTheSameFile() =>
        PathIdentity.SameFile(Make("a.dll"), Make("b.dll")).ShouldBeFalse();

    [Fact]
    public void AFileAndTheSameNameInAnotherFolder_AreNotTheSameFile() =>
        PathIdentity.SameFile(Make(Path.Combine("one", "a.dll")), Make(Path.Combine("two", "a.dll"))).ShouldBeFalse();

    [Fact]
    public void AFileThatIsNotThere_DoesNotThrow()
    {
        var missing = Path.Combine(_root, "missing", "a.dll");

        PathIdentity.SameFile(missing, missing).ShouldBeTrue();
        PathIdentity.SameFile(missing, Make("b.dll")).ShouldBeFalse();
    }

    [Fact]
    public void ABadPath_DoesNotThrow()
    {
        Should.NotThrow(() => PathIdentity.SameFile("a\0b", Make("a.dll")));
        Should.NotThrow(() => PathIdentity.SameFile(new string('x', 70_000), Make("a.dll")));
    }

    [Fact]
    public void TheExtendedLengthPrefix_IsTheSameFile()
    {
        if (!OperatingSystem.IsWindows())
            return; // \\?\ is a Windows spelling

        var file = Make("a.dll");

        PathIdentity.SameFile(file, @"\\?\" + file).ShouldBeTrue();
        PathIdentity.SameFile(@"\\?\" + file, file).ShouldBeTrue();
        PathIdentity.SameFile(@"\\?\" + file, Make("b.dll")).ShouldBeFalse();
    }

    [Fact]
    public void AVeryDeepPath_IsComparedWithoutOverflowingTheStack()
    {
        // A path of 20,000 components used to recurse once per component and end the process with a stack overflow
        // that no catch can stop. The longest path the OS accepts is far shorter; this one only has to not crash.
        var deep = Path.Combine(_root, string.Concat(Enumerable.Repeat("a" + Path.DirectorySeparatorChar, 20_000)), "x.dll");

        PathIdentity.SameFile(deep, deep).ShouldBeTrue();
        PathIdentity.SameFile(deep, deep + "2").ShouldBeFalse();
        PathIdentity.SameFile(deep, Make("b.dll")).ShouldBeFalse();
    }

    [Fact]
    public void AnExtendedLengthUncPath_IsTheUncPath()
    {
        if (!OperatingSystem.IsWindows())
            return; // the UNC form of the extended-length prefix is a Windows spelling

        // Normalised only: asking a host that is not there whether it is a link would wait for the network.
        PathIdentity.Normalize(@"\\?\UNC\server\share\dir\a.dll").ShouldBe(@"\\server\share\dir\a.dll");
        PathIdentity.Normalize(@"\\server\share\dir\a.dll").ShouldBe(@"\\server\share\dir\a.dll");
        PathIdentity.Normalize(@"\\?\C:\dir\a.dll").ShouldBe(@"C:\dir\a.dll");
    }

    [Fact]
    public void AnEightDotThreeShortName_IsExpandedByGetFullPath_SoItIsTheSameFile()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var file = Make("a long file name.dll");
        var shortName = ShortPathOf(file);
        if (shortName is null || !shortName.Contains('~'))
            return; // 8.3 names are switched off on this volume

        PathIdentity.SameFile(file, shortName).ShouldBeTrue();
    }

    /// <summary>The 8.3 form of an existing path, or null when the shell cannot say.</summary>
    private static string? ShortPathOf(string path)
    {
        try
        {
            var start = new System.Diagnostics.ProcessStartInfo("cmd.exe")
            {
                ArgumentList = { "/c", "for", "%I", "in", $"(\"{path}\")", "do", "@echo", "%~sI" },
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true
            };
            using var process = System.Diagnostics.Process.Start(start);
            var line = process?.StandardOutput.ReadLine();
            process?.WaitForExit();
            return string.IsNullOrWhiteSpace(line) ? null : line.Trim();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return null;
        }
    }

    [JunctionFact]
    public void AJunctionToTheFolder_IsTheFolder()
    {
        var file = Make(Path.Combine("real", "a.dll"));
        var link = Path.Combine(_root, "link");
        Junctions.TryCreate(link, Path.GetDirectoryName(file)!, out var error).ShouldBeTrue(error);

        PathIdentity.SameFile(file, Path.Combine(link, "a.dll")).ShouldBeTrue();
        PathIdentity.SameFile(Path.Combine(link, "a.dll"), file).ShouldBeTrue();
    }

    [JunctionFact]
    public void AJunctionAboveTheFolder_IsFollowedToo()
    {
        var file = Make(Path.Combine("real", "deep", "a.dll"));
        var link = Path.Combine(_root, "link");
        Junctions.TryCreate(link, Path.Combine(_root, "real"), out var error).ShouldBeTrue(error);

        PathIdentity.SameFile(file, Path.Combine(link, "deep", "a.dll")).ShouldBeTrue();
    }

    [JunctionFact]
    public void AJunctionToAnotherFolder_IsNotTheFile()
    {
        var file = Make(Path.Combine("real", "a.dll"));
        Make(Path.Combine("other", "a.dll"));
        var link = Path.Combine(_root, "link");
        Junctions.TryCreate(link, Path.Combine(_root, "other"), out var error).ShouldBeTrue(error);

        PathIdentity.SameFile(file, Path.Combine(link, "a.dll")).ShouldBeFalse();
    }

    [SymbolicLinkFact]
    public void ASymbolicLinkToTheFile_IsTheFile()
    {
        var file = Make(Path.Combine("real", "a.dll"));
        var link = Path.Combine(_root, "links", "a.dll");
        SymbolicLinks.TryCreate(link, file, out var error).ShouldBeTrue(error);

        PathIdentity.SameFile(file, link).ShouldBeTrue();
    }

    [SymbolicLinkFact]
    public void ASymbolicLinkToAnotherFile_IsNotTheFile()
    {
        var file = Make(Path.Combine("real", "a.dll"));
        var other = Make(Path.Combine("real", "b.dll"));
        var link = Path.Combine(_root, "links", "a.dll");
        SymbolicLinks.TryCreate(link, other, out var error).ShouldBeTrue(error);

        PathIdentity.SameFile(file, link).ShouldBeFalse();
        PathIdentity.SameFile(other, link).ShouldBeTrue();
    }

    [SymbolicLinkFact]
    public void ALoopOfLinks_EndsAndDoesNotThrow()
    {
        var a = Path.Combine(_root, "links", "a.dll");
        var b = Path.Combine(_root, "links", "b.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(a)!);
        var file = Make("real.dll");
        SymbolicLinks.TryCreate(a, b, out var error).ShouldBeTrue(error);
        SymbolicLinks.TryCreate(b, a, out error).ShouldBeTrue(error);

        Should.NotThrow(() => PathIdentity.SameFile(a, file));
    }
}
