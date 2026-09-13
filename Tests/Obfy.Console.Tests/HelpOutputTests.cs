using System.CommandLine;
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

    private string InvokeOutput(string args)
    {
        CommandLineTestHelpers.Invoke(_rootCommand, args, out var output);
        return output;
    }

    [Fact]
    public void Help_RootCommand_ShowsDescription()
    {
        var output = InvokeOutput("--help");

        output.ShouldNotBeNullOrEmpty();
        output.ShouldContain("Obfy - C# Obfuscation Tool");
    }

    [Fact]
    public void Help_RootCommand_ShowsInputArgument()
    {
        var output = InvokeOutput("--help");

        output.ShouldContain("input");
        output.ShouldContain("Input files to obfuscate");
    }

    [Fact]
    public void Help_RootCommand_ShowsOutputOption()
    {
        var output = InvokeOutput("--help");

        output.ShouldContain("--output");
        output.ShouldContain("-o");
    }

    [Fact]
    public void Help_RootCommand_ShowsLevelOption()
    {
        var output = InvokeOutput("--help");

        output.ShouldContain("--level");
        output.ShouldContain("-l");
        output.ShouldContain("minimal, standard, aggressive, or custom");
    }

    [Fact]
    public void Help_RootCommand_ShowsConfigOption()
    {
        var output = InvokeOutput("--help");

        output.ShouldContain("--config");
        output.ShouldContain("-c");
    }

    [Fact]
    public void Help_RootCommand_ShowsObfuscationOptions()
    {
        var output = InvokeOutput("--help");

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
        var output = InvokeOutput("--help");

        output.ShouldContain("--proxy-external");
        output.ShouldContain("--preserve-public");
        output.ShouldContain("--map");
        output.ShouldContain("--report");
        output.ShouldContain("--dry-run");
    }

    [Fact]
    public void Help_RootCommand_ShowsMergeOptions()
    {
        var output = InvokeOutput("--help");

        output.ShouldContain("--merge");
        output.ShouldContain("--internalize");
    }

    [Fact]
    public void Help_RootCommand_ShowsVerboseOptions()
    {
        var output = InvokeOutput("--help");

        output.ShouldContain("--verbose");
        output.ShouldContain("-v");
        output.ShouldContain("--no-logo");
    }

    [Fact]
    public void Help_RootCommand_ShowsConfigSubcommand()
    {
        var output = InvokeOutput("--help");

        output.ShouldContain("config");
    }

    [Fact]
    public void Help_ConfigCommand_ShowsDescription()
    {
        var output = InvokeOutput("config --help");

        output.ShouldContain("Configuration file operations");
    }

    [Fact]
    public void Help_ConfigCommand_ShowsSubcommands()
    {
        var output = InvokeOutput("config --help");

        output.ShouldContain("generate");
        output.ShouldContain("wizard");
    }

    [Fact]
    public void Help_ConfigGenerate_ShowsOptions()
    {
        var output = InvokeOutput("config generate --help");

        output.ShouldContain("Generate a default configuration file");
        output.ShouldContain("--output");
        output.ShouldContain("-o");
        output.ShouldContain("--level");
        output.ShouldContain("-l");
    }

    [Fact]
    public void Help_ConfigWizard_ShowsOptions()
    {
        var output = InvokeOutput("config wizard --help");

        output.ShouldContain("Interactive wizard");
        output.ShouldContain("--output");
        output.ShouldContain("-o");
        output.ShouldContain("--quick");
        output.ShouldContain("-q");
    }

    [Fact]
    public void Version_ShowsVersionInfo()
    {
        var output = InvokeOutput("--version");

        output.ShouldNotBeNullOrEmpty();
        // Version format should contain digits and dots
        output.Trim().ShouldMatch(@"^\d+\.\d+\.\d+");
    }
}
