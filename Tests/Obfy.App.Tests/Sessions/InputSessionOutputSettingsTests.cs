using Microsoft.Extensions.Logging.Abstractions;
using Obfy.App.Sessions;
using Obfy.App.Tests.Common;
using Shouldly;

namespace Obfy.App.Tests.Sessions;

/// <summary>The text the page sends for the output folder and the symbol map's path: what is accepted, and what resets.</summary>
public sealed class InputSessionOutputSettingsTests
{
    private readonly FakeSettings _settings = new();

    private InputSession Create() =>
        new(_settings, null, new FakeShell(), null, NullLogger<InputSession>.Instance, TimeSpan.Zero);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void SetSymbolMapPath_Empty_GoesBackToTheDefault_NotAnError(string cleared)
    {
        // The page sends {path:""} when the user empties the box.
        var session = Create();
        session.SetWriteSymbolMap(true);
        session.SetSymbolMapPath(@"maps\m.json");
        session.GetSnapshot().SymbolMapPath.ShouldBe(@"maps\m.json");

        Should.NotThrow(() => session.SetSymbolMapPath(cleared));

        var snapshot = session.GetSnapshot();
        snapshot.SymbolMapPath.ShouldBeEmpty();
        snapshot.ResolvedSymbolMapPath.ShouldNotBeNull().ShouldEndWith("symbolmap.json");
    }

    [Fact]
    public void SetSymbolMapPath_EmptyWhileAlreadyDefault_IsANoOp()
    {
        var session = Create();
        session.SetWriteSymbolMap(true);
        var before = session.GetSnapshot().Revision;

        Should.NotThrow(() => session.SetSymbolMapPath(""));

        session.GetSnapshot().Revision.ShouldBe(before);
    }

    [Theory]
    [InlineData(@"\\.\PhysicalDrive0")]
    [InlineData(@"\\?\C:\out")]
    [InlineData(@"\\?\UNC\server\share\out")]
    [InlineData("//./pipe/obfy")]
    [InlineData("//?/C:/out")]
    public void SetOutputFolder_DevicePath_IsRefused(string text)
    {
        var session = Create();

        var ex = Should.Throw<ObfyApiException>(() => session.SetOutputFolder(text));

        ex.Code.ShouldBe("output.invalidPath");
        session.GetSnapshot().OutputFolder.ShouldBeEmpty();
        _settings.OutputAssignments.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(@"\\.\PhysicalDrive0")]
    [InlineData(@"\\?\C:\out\map.json")]
    public void SetSymbolMapPath_DevicePath_IsRefused(string text)
    {
        // The run writes the map there, so a device path would be written to as a file.
        var session = Create();
        session.SetWriteSymbolMap(true);

        Should.Throw<ObfyApiException>(() => session.SetSymbolMapPath(text)).Code.ShouldBe("output.invalidPath");
        session.GetSnapshot().SymbolMapPath.ShouldBeEmpty();
    }

    [Fact]
    public void SetOutputFolder_OrdinaryUncShare_StillWorks()
    {
        // A probe that finds nothing, so the test does not wait on a network lookup.
        var session = new InputSession(_settings, null, new FakeShell(), null, NullLogger<InputSession>.Instance, TimeSpan.Zero, new NothingThere());

        session.SetOutputFolder(@"\\server\share\out");

        session.GetSnapshot().OutputFolder.ShouldBe(@"\\server\share\out");
    }

    [Fact]
    public void SetOutputFolder_APathThatIsAFile_IsStillRefused()
    {
        var session = new InputSession(_settings, null, new FakeShell(), null, NullLogger<InputSession>.Instance, TimeSpan.Zero, new EverythingIsAFile());

        Should.Throw<ObfyApiException>(() => session.SetOutputFolder(@"C:\some\file.dll")).Code.ShouldBe("output.isFile");
    }

    private sealed class NothingThere : Obfy.App.Services.IPathProbe
    {
        public bool FileExists(string path) => false;

        public bool DirectoryExists(string path) => false;
    }

    private sealed class EverythingIsAFile : Obfy.App.Services.IPathProbe
    {
        public bool FileExists(string path) => true;

        public bool DirectoryExists(string path) => false;
    }
}
