using Obfy.App.Services;
using Shouldly;

namespace Obfy.App.Tests.Services;

/// <summary>
/// The process-start parser. The first five cases are those of the former <c>StartupCommandLineTests</c> (the duplicate
/// class in <c>Obfy.UI</c>), ported to the parser the app actually uses.
/// </summary>
public class StartupArgumentsTests
{
    // ---- ported from StartupCommandLineTests ----

    [Fact]
    public void Parse_EmptyArgs_ReturnsNoFilesOrOutput()
    {
        var parsed = StartupArguments.Parse([]);

        parsed.Files.ShouldBeEmpty();
        parsed.OutputDirectory.ShouldBeNull();
    }

    [Fact]
    public void Parse_FilesAndShortOutput_ReturnsBoth()
    {
        var parsed = StartupArguments.Parse(["MyApp.dll", "Lib.dll", "-o", "output"]);

        parsed.Files.ShouldBe(["MyApp.dll", "Lib.dll"]);
        parsed.OutputDirectory.ShouldBe("output");
    }

    [Fact]
    public void Parse_LongOutputFlag_IsAccepted()
    {
        var parsed = StartupArguments.Parse(["--output", @"C:\out", "App.exe"]);

        parsed.Files.ShouldBe(["App.exe"]);
        parsed.OutputDirectory.ShouldBe(@"C:\out");
    }

    [Fact]
    public void Parse_UnknownFlags_AreIgnored()
    {
        var parsed = StartupArguments.Parse(["--theme", "App.dll", "-v"]);

        parsed.Files.ShouldBe(["App.dll"]);
        parsed.OutputDirectory.ShouldBeNull();
    }

    [Fact]
    public void Parse_OutputFlagWithoutValue_IsIgnored()
    {
        var parsed = StartupArguments.Parse(["App.dll", "-o"]);

        parsed.Files.ShouldBe(["App.dll"]);
        parsed.OutputDirectory.ShouldBeNull();
    }

    // ---- the defects ----

    [Theory]
    [InlineData("--output=out")]
    [InlineData("--output:out")]
    [InlineData("-o=out")]
    [InlineData("-o:out")]
    public void Parse_OutputWithAnInlineValue_IsAccepted(string arg)
    {
        var parsed = StartupArguments.Parse(["App.dll", arg]);

        parsed.Files.ShouldBe(["App.dll"]);
        parsed.OutputDirectory.ShouldBe("out");
    }

    [Fact]
    public void Parse_InlineValueMayHoldADriveLetterAndEqualsSigns()
    {
        StartupArguments.Parse([@"--output=C:\out\a=b"]).OutputDirectory.ShouldBe(@"C:\out\a=b");
        StartupArguments.Parse([@"--output:C:\out"]).OutputDirectory.ShouldBe(@"C:\out");
    }

    [Fact]
    public void Parse_OutputFollowedByAnotherFlag_DoesNotTakeTheFlagAsTheFolder()
    {
        var parsed = StartupArguments.Parse(["-o", "-x", "App.dll"]);

        parsed.OutputDirectory.ShouldBeNull();
        parsed.Files.ShouldBe(["App.dll"]);
    }

    [Fact]
    public void Parse_OutputAsTheLastArgument_IsIgnoredAndNeverAFile()
    {
        StartupArguments.Parse(["App.dll", "--output"]).Files.ShouldBe(["App.dll"]);
        StartupArguments.Parse(["App.dll", "--output"]).OutputDirectory.ShouldBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_AnEmptyOutputValue_IsNoOutputFolder(string value)
    {
        StartupArguments.Parse(["-o", value]).OutputDirectory.ShouldBeNull();
        StartupArguments.Parse(["--output=" + value]).OutputDirectory.ShouldBeNull();
    }

    [Fact]
    public void Parse_TheLastOutputWins()
    {
        StartupArguments.Parse(["-o", "a", "--output=b"]).OutputDirectory.ShouldBe("b");
    }

    [Fact]
    public void Parse_OutputValueIsNotAFile()
    {
        var parsed = StartupArguments.Parse(["-o", "out", "a.dll", "--output=more", "b.dll"]);

        parsed.Files.ShouldBe(["a.dll", "b.dll"]);
    }

    [Fact]
    public void Parse_AFlagLookalikeInsideAFileName_IsStillAFile()
    {
        StartupArguments.Parse([@"C:\x\-o.dll", "my--output=1.dll"]).Files.ShouldBe([@"C:\x\-o.dll", "my--output=1.dll"]);
    }
}
