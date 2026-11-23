using System.CommandLine;
using System.Text.Json;
using Obfy.Console;
using Obfy.Core.Models;
using Shouldly;

namespace Obfy.Console.Tests;

public class IntegrationParseTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), $"ObfyParse_{Guid.NewGuid():N}");

    public IntegrationParseTests() => Directory.CreateDirectory(_tempDirectory);

    public void Dispose()
    {
        try { Directory.Delete(_tempDirectory, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public void Parse_ComplexCommand_AllOptionsCorrect()
    {
        var assemblyPath = ConsoleTestAssembly.Create(_tempDirectory, "ComplexTest.dll");
        var outputDir = Path.Combine(_tempDirectory, "complex-output");
        var configPath = Path.Combine(_tempDirectory, "config.json");
        var mapPath = Path.Combine(_tempDirectory, "map.json");
        var reportPath = Path.Combine(_tempDirectory, "report.html");
        File.WriteAllText(configPath, "{}");

        var rootCommand = Program.CreateRootCommand();
        var parseResult = rootCommand.Parse(
            $"\"{assemblyPath}\" -o \"{outputDir}\" -c \"{configPath}\" " +
            $"--level aggressive --string-encrypt --control-flow --rename " +
            $"--anti-debug --preserve-public --map \"{mapPath}\" --report \"{reportPath}\" " +
            $"--verbose --no-logo");

        parseResult.Errors.ShouldBeEmpty();
        var files = parseResult.ArgumentValue<FileInfo[]>("input");
        files.Length.ShouldBe(1);
        files[0].FullName.ShouldBe(assemblyPath);
        parseResult.OptionValue<string>("--level").ShouldBe("aggressive");
        parseResult.OptionValue<bool>("--string-encrypt").ShouldBeTrue();
        parseResult.OptionValue<bool>("--control-flow").ShouldBeTrue();
        parseResult.OptionValue<bool>("--rename").ShouldBeTrue();
        parseResult.OptionValue<bool>("--anti-debug").ShouldBeTrue();
        parseResult.OptionValue<bool>("--preserve-public").ShouldBeTrue();
        parseResult.OptionValue<bool>("--verbose").ShouldBeTrue();
        parseResult.OptionValue<bool>("--no-logo").ShouldBeTrue();
    }

    [Fact]
    public void Parse_MergeCommand_AllOptionsCorrect()
    {
        var assembly1 = ConsoleTestAssembly.Create(_tempDirectory, "App.dll");
        var assembly2 = ConsoleTestAssembly.Create(_tempDirectory, "Lib.dll");
        var outputDir = Path.Combine(_tempDirectory, "merge-output");

        var rootCommand = Program.CreateRootCommand();
        var parseResult = rootCommand.Parse(
            $"\"{assembly1}\" \"{assembly2}\" -o \"{outputDir}\" --merge --internalize");

        parseResult.Errors.ShouldBeEmpty();
        parseResult.ArgumentValue<FileInfo[]>("input").Length.ShouldBe(2);
        parseResult.OptionValue<bool>("--merge").ShouldBeTrue();
        parseResult.OptionValue<bool>("--internalize").ShouldBeTrue();
    }

    [Fact]
    public void Parse_WithConfigFile_ParsesCorrectly()
    {
        var assemblyPath = ConsoleTestAssembly.Create(_tempDirectory, "ConfigLoadTest.dll");
        var configPath = Path.Combine(_tempDirectory, "obfy.json");
        var settings = ObfySettings.ForLevel(ObfuscationLevel.Aggressive);
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });
        File.WriteAllText(configPath, json);

        var rootCommand = Program.CreateRootCommand();
        var parseResult = rootCommand.Parse([assemblyPath, "-c", configPath]);

        parseResult.Errors.ShouldBeEmpty();
        var config = parseResult.OptionValue<FileInfo?>("--config");
        config.ShouldNotBeNull();
        config.Exists.ShouldBeTrue();
    }
}
