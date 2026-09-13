using System.CommandLine;
using System.CommandLine.IO;
using System.CommandLine.Parsing;
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
        // Arrange & Act
        var parseResult = _rootCommand.Parse("");

        // Assert
        parseResult.Errors.ShouldNotBeEmpty();
        // System.CommandLine says "Required argument missing" or "Required command was not provided"
        parseResult.Errors.ShouldContain(e =>
            e.Message.Contains("Required") || e.Message.Contains("argument"));
    }

    [Fact]
    public void Parse_UnrecognizedTokens_AreTreatedAsFileArguments()
    {
        // Arrange & Act
        // System.CommandLine with FileInfo[] argument treats unknown options
        // as additional file arguments (since the argument has OneOrMore arity)
        var parseResult = _rootCommand.Parse("input.dll --unknown-option");

        // Assert - no parse errors, but the "option" is treated as a file argument
        var files = parseResult.GetValueForArgument(Program.InputArgument);
        files.ShouldNotBeNull();
        files.Length.ShouldBe(2); // input.dll and --unknown-option
    }

    [Fact]
    public void Parse_TripleDash_IsTreatedAsFileArgument()
    {
        // Arrange & Act
        var parseResult = _rootCommand.Parse("input.dll ---triple-dash");

        // Assert - System.CommandLine treats this as another file argument
        var files = parseResult.GetValueForArgument(Program.InputArgument);
        files.ShouldNotBeNull();
        files.Length.ShouldBe(2);
    }

    [Fact]
    public void Invoke_WithMissingInputFile_ReturnsNonZeroExitCode()
    {
        // Arrange
        var console = new TestConsole();

        // Act
        var exitCode = _rootCommand.Invoke("", console);

        // Assert
        exitCode.ShouldNotBe(0);
    }

    [Fact]
    public void Invoke_WithUnknownOption_TreatsAsFileArgument()
    {
        // Arrange
        var console = new TestConsole();

        // Act — CreateRootCommand uses a no-op handler so parse-only invoke is 0.
        // Product process behavior is covered by Program.Main tests in IntegrationProcessTests.
        var exitCode = _rootCommand.Invoke("input.dll --fake-option", console);

        exitCode.ShouldBe(0);
    }

    [Fact]
    public void Parse_OutputWithoutValue_ReturnsError()
    {
        // Arrange & Act
        var parseResult = _rootCommand.Parse("input.dll --output");

        // Assert
        parseResult.Errors.ShouldNotBeEmpty();
    }

    [Fact]
    public void Parse_ConfigWithoutValue_ReturnsError()
    {
        // Arrange & Act
        var parseResult = _rootCommand.Parse("input.dll --config");

        // Assert
        parseResult.Errors.ShouldNotBeEmpty();
    }

    [Fact]
    public void Parse_LevelWithoutValue_ReturnsError()
    {
        // Arrange & Act
        var parseResult = _rootCommand.Parse("input.dll --level");

        // Assert
        parseResult.Errors.ShouldNotBeEmpty();
    }

    [Fact]
    public void Parse_MapWithoutValue_ReturnsError()
    {
        // Arrange & Act
        var parseResult = _rootCommand.Parse("input.dll --map");

        // Assert
        parseResult.Errors.ShouldNotBeEmpty();
    }

    [Fact]
    public void Parse_ReportWithoutValue_ReturnsError()
    {
        // Arrange & Act
        var parseResult = _rootCommand.Parse("input.dll --report");

        // Assert
        parseResult.Errors.ShouldNotBeEmpty();
    }

    [Fact]
    public void Parse_InvalidSubcommand_ReturnsError()
    {
        // Arrange & Act
        var parseResult = _rootCommand.Parse("config invalid");

        // Assert
        parseResult.Errors.ShouldNotBeEmpty();
    }

    [Fact]
    public void Invoke_InvalidSubcommand_ShowsError()
    {
        // Arrange
        var console = new TestConsole();

        // Act
        var exitCode = _rootCommand.Invoke("config invalid", console);
        var output = console.Error.ToString() + console.Out.ToString();

        // Assert
        exitCode.ShouldNotBe(0);
    }

    [Fact]
    public void Parse_DuplicateOption_ReturnsError()
    {
        // Arrange & Act
        var parseResult = _rootCommand.Parse("input.dll --level minimal --level aggressive");

        // Assert
        // System.CommandLine doesn't allow duplicate single-value options
        parseResult.Errors.ShouldNotBeEmpty();
    }

    [Fact]
    public void Parse_ConflictingShortAndLongOption_BothWork()
    {
        // Arrange & Act - using -v and --verbose
        var parseResult = _rootCommand.Parse("input.dll -v --verbose");

        // Assert - should work (same option specified twice)
        var verbose = parseResult.GetValueForOption(Program.VerboseOption);
        verbose.ShouldBeTrue();
    }
}
