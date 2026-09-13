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

        var exitCode = await CommandLineTestHelpers.InvokeAsync(rootCommand, $"config generate -o {outputPath}");

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

        var exitCode = await CommandLineTestHelpers.InvokeAsync(rootCommand, $"config generate -o {outputPath} -l minimal");

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

        var exitCode = await CommandLineTestHelpers.InvokeAsync(rootCommand, $"config generate -o {outputPath} -l aggressive");

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

        var exitCode = await CommandLineTestHelpers.InvokeAsync(rootCommand, $"config generate -o {outputPath}");

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

        var rootCommand = Program.CreateRootCommand();
        var exitCode = await CommandLineTestHelpers.InvokeAsync(
            rootCommand,
            $"\"{assemblyPath}\" -o \"{outputDir}\" --dry-run --no-logo");

        exitCode.ShouldBe(0);
        File.Exists(Path.Combine(outputDir, "DryRunTest.dll")).ShouldBeFalse();
    }

    [Fact]
    public async Task Help_ReturnsZeroExitCode()
    {
        var exitCode = await CommandLineTestHelpers.InvokeAsync(Program.CreateRootCommand(), "--help");
        exitCode.ShouldBe(0);
    }

    [Fact]
    public async Task Version_ReturnsZeroExitCode()
    {
        var exitCode = await CommandLineTestHelpers.InvokeAsync(Program.CreateRootCommand(), "--version");
        exitCode.ShouldBe(0);
    }

    [Fact]
    public async Task ConfigGenerateHelp_ReturnsZeroExitCode()
    {
        var exitCode = await CommandLineTestHelpers.InvokeAsync(Program.CreateRootCommand(), "config generate --help");
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
