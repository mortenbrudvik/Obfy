using System.CommandLine;
using System.CommandLine.IO;
using System.Text.Json;
using dnlib.DotNet;
using Obfy.Console;
using Obfy.Core.Models;
using Shouldly;

namespace Obfy.Console.Tests;

public class IntegrationProcessTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), $"ObfyProcess_{Guid.NewGuid():N}");

    public IntegrationProcessTests() => Directory.CreateDirectory(_tempDirectory);

    public void Dispose()
    {
        try { Directory.Delete(_tempDirectory, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public async Task ConfigGenerate_CreatesDefaultConfigFile()
    {
        var outputPath = Path.Combine(_tempDirectory, "test-config.json");
        var rootCommand = Program.CreateRootCommand();
        var console = new TestConsole();

        var exitCode = await rootCommand.InvokeAsync($"config generate -o {outputPath}", console);

        exitCode.ShouldBe(0);
        File.Exists(outputPath).ShouldBeTrue();
        var json = await File.ReadAllTextAsync(outputPath);
        json.ShouldNotBeNullOrEmpty();
        var settings = JsonSerializer.Deserialize<ObfySettings>(json, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        settings.ShouldNotBeNull();
        settings!.Packing.Enabled.ShouldBeFalse();
        json.ShouldContain("\"packing\"");
    }

    [Fact]
    public async Task ConfigGenerate_WithMinimalLevel_CreatesMinimalConfig()
    {
        var outputPath = Path.Combine(_tempDirectory, "minimal-config.json");
        var rootCommand = Program.CreateRootCommand();
        var console = new TestConsole();

        var exitCode = await rootCommand.InvokeAsync($"config generate -o {outputPath} -l minimal", console);

        exitCode.ShouldBe(0);
        var json = await File.ReadAllTextAsync(outputPath);
        var settings = JsonSerializer.Deserialize<ObfySettings>(json, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        settings.ShouldNotBeNull();
        settings!.StringEncryption.Enabled.ShouldBeFalse();
        settings.ControlFlow.Enabled.ShouldBeFalse();
    }

    [Fact]
    public async Task ConfigGenerate_WithAggressiveLevel_CreatesAggressiveConfig()
    {
        var outputPath = Path.Combine(_tempDirectory, "aggressive-config.json");
        var rootCommand = Program.CreateRootCommand();
        var console = new TestConsole();

        var exitCode = await rootCommand.InvokeAsync($"config generate -o {outputPath} -l aggressive", console);

        exitCode.ShouldBe(0);
        var json = await File.ReadAllTextAsync(outputPath);
        var settings = JsonSerializer.Deserialize<ObfySettings>(json, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        settings.ShouldNotBeNull();
        settings!.StringEncryption.Enabled.ShouldBeTrue();
        settings.ControlFlow.Enabled.ShouldBeTrue();
        settings.SymbolRenaming.Enabled.ShouldBeTrue();
        settings.Protection.AntiDump.ShouldBeTrue();
        settings.Protection.ReferenceProxy.ShouldBeTrue();
        settings.ConstantEncryption.Enabled.ShouldBeTrue();
    }

    [Fact]
    public async Task ConfigGenerate_IncludesSchemaProperty()
    {
        var outputPath = Path.Combine(_tempDirectory, "schema-config.json");
        var rootCommand = Program.CreateRootCommand();
        var console = new TestConsole();

        var exitCode = await rootCommand.InvokeAsync($"config generate -o {outputPath}", console);

        exitCode.ShouldBe(0);
        var json = await File.ReadAllTextAsync(outputPath);
        using var document = JsonDocument.Parse(json);
        document.RootElement.TryGetProperty("$schema", out var schema).ShouldBeTrue();
        schema.GetString().ShouldBe("https://raw.githubusercontent.com/mortenbrudvik/Obfy/main/schemas/obfy.schema.json");
    }

    [Fact]
    public async Task Obfuscate_WithDryRun_DoesNotWriteOutput()
    {
        var assemblyPath = ConsoleTestAssembly.Create(_tempDirectory, "DryRunTest.dll");
        var outputDir = Path.Combine(_tempDirectory, "dry-run-output");
        Directory.CreateDirectory(outputDir);

        var exitCode = await Program.Main([assemblyPath, "-o", outputDir, "--dry-run", "--no-logo"]);

        exitCode.ShouldBe(0);
        File.Exists(Path.Combine(outputDir, "DryRunTest.dll")).ShouldBeFalse();
    }

    [Fact]
    public async Task Main_MissingInputFile_ReturnsNonZero()
    {
        var outputDir = Path.Combine(_tempDirectory, "missing-out");
        Directory.CreateDirectory(outputDir);

        var exitCode = await Program.Main([
            Path.Combine(_tempDirectory, "no-such.dll"), "-o", outputDir, "--no-logo"
        ]);

        exitCode.ShouldNotBe(0);
        Directory.GetFiles(outputDir).ShouldBeEmpty();
    }

    [Fact]
    public async Task Main_MalformedConfigJson_ReturnsNonZero()
    {
        var input = ConsoleTestAssembly.Create(_tempDirectory, "BadJson.dll");
        var config = Path.Combine(_tempDirectory, "broken.json");
        File.WriteAllText(config, "{ not json");
        var outputDir = Path.Combine(_tempDirectory, "bad-json-out");
        Directory.CreateDirectory(outputDir);

        var exitCode = await Program.Main([input, "-c", config, "-o", outputDir, "--no-logo"]);

        exitCode.ShouldNotBe(0);
        File.Exists(Path.Combine(outputDir, "BadJson.dll")).ShouldBeFalse();
    }

    [Fact]
    public async Task Main_UnknownLevel_ReturnsNonZero()
    {
        var input = ConsoleTestAssembly.Create(_tempDirectory, "BadLevel.dll");
        var outputDir = Path.Combine(_tempDirectory, "bad-level-out");
        Directory.CreateDirectory(outputDir);

        var exitCode = await Program.Main([input, "-o", outputDir, "-l", "banana", "--no-logo"]);

        exitCode.ShouldNotBe(0);
        File.Exists(Path.Combine(outputDir, "BadLevel.dll")).ShouldBeFalse();
    }

    [Fact]
    public async Task Help_ReturnsZeroExitCode()
    {
        var exitCode = await Program.CreateRootCommand().InvokeAsync("--help", new TestConsole());
        exitCode.ShouldBe(0);
    }

    [Fact]
    public async Task Version_ReturnsZeroExitCode()
    {
        var exitCode = await Program.CreateRootCommand().InvokeAsync("--version", new TestConsole());
        exitCode.ShouldBe(0);
    }

    [Fact]
    public async Task ConfigGenerateHelp_ReturnsZeroExitCode()
    {
        var exitCode = await Program.CreateRootCommand().InvokeAsync("config generate --help", new TestConsole());
        exitCode.ShouldBe(0);
    }

    [Fact]
    public async Task Main_RunsRealObfuscationHandler_EndToEnd()
    {
        var input = ConsoleTestAssembly.Create(_tempDirectory, "MainE2E.dll");
        var outputDir = Path.Combine(_tempDirectory, "out");

        var exitCode = await Program.Main([input, "-o", outputDir, "-l", "minimal"]);

        exitCode.ShouldBe(0);
        var outputPath = Path.Combine(outputDir, "MainE2E.dll");
        File.Exists(outputPath).ShouldBeTrue();
        using var outModule = ModuleDefMD.Load(outputPath);
        outModule.Types.Any(t => t.Name == "TestClass").ShouldBeFalse();
    }
}
