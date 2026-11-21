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
