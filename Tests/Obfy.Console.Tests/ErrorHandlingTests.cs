using System.CommandLine;
using Obfy.Console;
using Shouldly;

namespace Obfy.Console.Tests;

public class ErrorHandlingTests
{
    private readonly RootCommand _rootCommand;

    public ErrorHandlingTests()
    {
        _rootCommand = Program.CreateRootCommand();
    }

    [Fact]
    public void Parse_MissingRequiredArgument_ReturnsError()
    {
        var parseResult = _rootCommand.Parse("");

        parseResult.Errors.ShouldNotBeEmpty();
        parseResult.Errors.ShouldContain(e =>
            e.Message.Contains("Required") || e.Message.Contains("argument"));
    }

    [Fact]
    public void Parse_UnrecognizedTokens_AreTreatedAsFileArguments()
    {
        // System.CommandLine with FileInfo[] argument treats unknown options
        // as additional file arguments (since the argument has OneOrMore arity)
        var parseResult = _rootCommand.Parse("input.dll --unknown-option");

        var files = parseResult.GetRequiredValue(Program.InputArgument);
        files.ShouldNotBeNull();
        files.Length.ShouldBe(2); // input.dll and --unknown-option
    }

    [Fact]
    public void Parse_TripleDash_IsTreatedAsFileArgument()
    {
        var parseResult = _rootCommand.Parse("input.dll ---triple-dash");

        var files = parseResult.GetRequiredValue(Program.InputArgument);
        files.ShouldNotBeNull();
        files.Length.ShouldBe(2);
    }

    [Fact]
    public void Invoke_WithMissingInputFile_ReturnsNonZeroExitCode()
    {
        var exitCode = CommandLineTestHelpers.Invoke(_rootCommand, "", out _);

        exitCode.ShouldNotBe(0);
    }

    [Fact]
    public void Invoke_WithUnknownOption_TreatsAsFileArgument()
    {
        // CreateRootCommand uses a no-op action so parse-only invoke is 0.
        // Product process behavior is covered by Program.Main tests in IntegrationProcessTests.
        var exitCode = CommandLineTestHelpers.Invoke(_rootCommand, "input.dll --fake-option", out _);

        exitCode.ShouldBe(0);
    }

    [Fact]
    public void Parse_OutputWithoutValue_ReturnsError()
    {
        var parseResult = _rootCommand.Parse("input.dll --output");

        parseResult.Errors.ShouldNotBeEmpty();
    }

    [Fact]
    public void Parse_ConfigWithoutValue_ReturnsError()
    {
        var parseResult = _rootCommand.Parse("input.dll --config");

        parseResult.Errors.ShouldNotBeEmpty();
    }

    [Fact]
    public void Parse_LevelWithoutValue_ReturnsError()
    {
        var parseResult = _rootCommand.Parse("input.dll --level");

        parseResult.Errors.ShouldNotBeEmpty();
    }

    [Fact]
    public void Parse_MapWithoutValue_ReturnsError()
    {
        var parseResult = _rootCommand.Parse("input.dll --map");

        parseResult.Errors.ShouldNotBeEmpty();
    }

    [Fact]
    public void Parse_ReportWithoutValue_ReturnsError()
    {
        var parseResult = _rootCommand.Parse("input.dll --report");

        parseResult.Errors.ShouldNotBeEmpty();
    }

    [Fact]
    public void Parse_InvalidSubcommand_ReturnsError()
    {
        var parseResult = _rootCommand.Parse("config invalid");

        parseResult.Errors.ShouldNotBeEmpty();
    }

    [Fact]
    public void Invoke_InvalidSubcommand_ShowsError()
    {
        var exitCode = CommandLineTestHelpers.Invoke(_rootCommand, "config invalid", out var output);

        exitCode.ShouldNotBe(0);
        output.ShouldNotBeNull();
    }

    [Fact]
    public void Parse_DuplicateOption_ReturnsError()
    {
        var parseResult = _rootCommand.Parse("input.dll --level minimal --level aggressive");

        parseResult.Errors.ShouldNotBeEmpty();
    }

    [Fact]
    public void Parse_ConflictingShortAndLongOption_BothWork()
    {
        var parseResult = _rootCommand.Parse("input.dll -v --verbose");

        var verbose = parseResult.GetValue(Program.VerboseOption);
        verbose.ShouldBeTrue();
    }
}
