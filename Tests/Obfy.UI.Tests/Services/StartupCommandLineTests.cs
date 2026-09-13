using Obfy.UI.Services;
using Shouldly;

namespace Obfy.UI.Tests.Services;

public class StartupCommandLineTests
{
    [Fact]
    public void Parse_EmptyArgs_ReturnsNoFilesOrOutput()
    {
        var parsed = StartupCommandLine.Parse([]);

        parsed.Files.ShouldBeEmpty();
        parsed.OutputDirectory.ShouldBeNull();
    }

    [Fact]
    public void Parse_FilesAndShortOutput_ReturnsBoth()
    {
        var parsed = StartupCommandLine.Parse(["MyApp.dll", "Lib.dll", "-o", "output"]);

        parsed.Files.ShouldBe(["MyApp.dll", "Lib.dll"]);
        parsed.OutputDirectory.ShouldBe("output");
    }

    [Fact]
    public void Parse_LongOutputFlag_IsAccepted()
    {
        var parsed = StartupCommandLine.Parse(["--output", @"C:\out", "App.exe"]);

        parsed.Files.ShouldBe(["App.exe"]);
        parsed.OutputDirectory.ShouldBe(@"C:\out");
    }

    [Fact]
    public void Parse_UnknownFlags_AreIgnored()
    {
        var parsed = StartupCommandLine.Parse(["--theme", "App.dll", "-v"]);

        parsed.Files.ShouldBe(["App.dll"]);
        parsed.OutputDirectory.ShouldBeNull();
    }

    [Fact]
    public void Parse_OutputFlagWithoutValue_IsIgnored()
    {
        var parsed = StartupCommandLine.Parse(["App.dll", "-o"]);

        parsed.Files.ShouldBe(["App.dll"]);
        parsed.OutputDirectory.ShouldBeNull();
    }
}
