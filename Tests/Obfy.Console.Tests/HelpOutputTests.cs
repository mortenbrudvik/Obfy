using System.CommandLine;
using System.CommandLine.IO;
using Obfy.Console;
using Shouldly;

namespace Obfy.Console.Tests;

public class HelpOutputTests
{
    private readonly RootCommand _rootCommand;

    public HelpOutputTests()
    {
        _rootCommand = Program.CreateRootCommand();
    }

    [Fact]
    public void Help_RootCommand_ShowsDescription()
    {
        // Arrange
        var console = new TestConsole();

        // Act
        _rootCommand.Invoke("--help", console);
        var output = console.Out.ToString();

        // Assert
        output.ShouldNotBeNullOrEmpty();
        output.ShouldContain("Obfy - C# Obfuscation Tool");
    }

    [Fact]
    public void Help_RootCommand_ShowsInputArgument()
    {
        // Arrange
        var console = new TestConsole();

        // Act
        _rootCommand.Invoke("--help", console);
        var output = console.Out.ToString();

        // Assert
        output.ShouldContain("input");
        output.ShouldContain("Input files to obfuscate");
    }

    [Fact]
    public void Help_RootCommand_ShowsOutputOption()
    {
        // Arrange
        var console = new TestConsole();

        // Act
        _rootCommand.Invoke("--help", console);
        var output = console.Out.ToString();

        // Assert
        output.ShouldContain("--output");
        output.ShouldContain("-o");
    }

    [Fact]
    public void Help_RootCommand_ShowsLevelOption()
    {
        // Arrange
        var console = new TestConsole();

        // Act
        _rootCommand.Invoke("--help", console);
        var output = console.Out.ToString();

        // Assert
        output.ShouldContain("--level");
        output.ShouldContain("-l");
        output.ShouldContain("minimal, standard, aggressive, or custom");
    }

    [Fact]
    public void Help_RootCommand_ShowsConfigOption()
    {
        // Arrange
        var console = new TestConsole();

        // Act
        _rootCommand.Invoke("--help", console);
        var output = console.Out.ToString();

        // Assert
        output.ShouldContain("--config");
        output.ShouldContain("-c");
    }

    [Fact]
    public void Help_RootCommand_ShowsObfuscationOptions()
    {
        // Arrange
        var console = new TestConsole();

        // Act
        _rootCommand.Invoke("--help", console);
        var output = console.Out.ToString();

        // Assert
        output.ShouldContain("--string-encrypt");
        output.ShouldContain("--control-flow");
        output.ShouldContain("--rename");
        output.ShouldContain("--anti-debug");
        output.ShouldContain("--anti-dump");
        output.ShouldContain("--encrypt-methods");
        output.ShouldContain("--reference-proxy");
        output.ShouldContain("--proxy-external");
        output.ShouldContain("--encrypt-constants");
        output.ShouldContain("--no-control-flow");
        output.ShouldContain("--strip-metadata");
        output.ShouldContain("--encrypt-resources");
        output.ShouldContain("--watermark-id");
    }

    [Fact]
    public void Help_RootCommand_ShowsProtectionOptions()
    {
        // Arrange
        var console = new TestConsole();

        // Act
        _rootCommand.Invoke("--help", console);
        var output = console.Out.ToString();

        // Assert
        output.ShouldContain("--proxy-external");
        output.ShouldContain("--preserve-public");
        output.ShouldContain("--map");
        output.ShouldContain("--report");
        output.ShouldContain("--dry-run");
    }

    [Fact]
    public void Help_RootCommand_ShowsMergeOptions()
    {
        // Arrange
        var console = new TestConsole();

        // Act
        _rootCommand.Invoke("--help", console);
        var output = console.Out.ToString();

        // Assert
        output.ShouldContain("--merge");
        output.ShouldContain("--internalize");
    }

    [Fact]
    public void Help_RootCommand_ShowsVerboseOptions()
    {
        // Arrange
        var console = new TestConsole();

        // Act
        _rootCommand.Invoke("--help", console);
        var output = console.Out.ToString();

        // Assert
        output.ShouldContain("--verbose");
        output.ShouldContain("-v");
        output.ShouldContain("--no-logo");
    }

    [Fact]
    public void Help_RootCommand_ShowsConfigSubcommand()
    {
        // Arrange
        var console = new TestConsole();

        // Act
        _rootCommand.Invoke("--help", console);
        var output = console.Out.ToString();

        // Assert
        output.ShouldContain("config");
    }

    [Fact]
    public void Help_ConfigCommand_ShowsDescription()
    {
        // Arrange
        var console = new TestConsole();

        // Act
        _rootCommand.Invoke("config --help", console);
        var output = console.Out.ToString();

        // Assert
        output.ShouldContain("Configuration file operations");
    }

    [Fact]
    public void Help_ConfigCommand_ShowsSubcommands()
    {
        // Arrange
        var console = new TestConsole();

        // Act
        _rootCommand.Invoke("config --help", console);
        var output = console.Out.ToString();

        // Assert
        output.ShouldContain("generate");
        output.ShouldContain("wizard");
    }

    [Fact]
    public void Help_ConfigGenerate_ShowsOptions()
    {
        // Arrange
        var console = new TestConsole();

        // Act
        _rootCommand.Invoke("config generate --help", console);
        var output = console.Out.ToString();

        // Assert
        output.ShouldContain("Generate a default configuration file");
        output.ShouldContain("--output");
        output.ShouldContain("-o");
        output.ShouldContain("--level");
        output.ShouldContain("-l");
    }

    [Fact]
    public void Help_ConfigWizard_ShowsOptions()
    {
        // Arrange
        var console = new TestConsole();

        // Act
        _rootCommand.Invoke("config wizard --help", console);
        var output = console.Out.ToString();

        // Assert
        output.ShouldContain("Interactive wizard");
        output.ShouldContain("--output");
        output.ShouldContain("-o");
        output.ShouldContain("--quick");
        output.ShouldContain("-q");
    }

    [Fact]
    public void Version_ShowsVersionInfo()
    {
        // Arrange
        var console = new TestConsole();

        // Act
        _rootCommand.Invoke("--version", console);
        var output = console.Out.ToString();

        // Assert
        output.ShouldNotBeNullOrEmpty();
        // Version format should contain digits and dots
        output.Trim().ShouldMatch(@"^\d+\.\d+\.\d+");
    }
}
