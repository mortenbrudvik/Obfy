using System.CommandLine;
using System.CommandLine.IO;
using System.Text.Json;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using Obfy.Console;
using Obfy.Core.Models;
using Shouldly;

namespace Obfy.Console.Tests;

public class IntegrationTests : IDisposable
{
    private readonly string _tempDirectory;

    public IntegrationTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"ObfyConsoleTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    #region Config Generate Tests

    [Fact]
    public async Task ConfigGenerate_CreatesDefaultConfigFile()
    {
        // Arrange
        var outputPath = Path.Combine(_tempDirectory, "test-config.json");
        var rootCommand = Program.CreateRootCommand();
        var console = new TestConsole();

        // Act
        var exitCode = await rootCommand.InvokeAsync($"config generate -o {outputPath}", console);

        // Assert
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
        // Arrange
        var outputPath = Path.Combine(_tempDirectory, "minimal-config.json");
        var rootCommand = Program.CreateRootCommand();
        var console = new TestConsole();

        // Act
        var exitCode = await rootCommand.InvokeAsync($"config generate -o {outputPath} -l minimal", console);

        // Assert
        exitCode.ShouldBe(0);
        File.Exists(outputPath).ShouldBeTrue();

        var json = await File.ReadAllTextAsync(outputPath);
        var settings = JsonSerializer.Deserialize<ObfySettings>(json, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        settings.ShouldNotBeNull();
        // Minimal level should have most protections disabled
        settings.StringEncryption.Enabled.ShouldBeFalse();
        settings.ControlFlow.Enabled.ShouldBeFalse();
    }

    [Fact]
    public async Task ConfigGenerate_WithAggressiveLevel_CreatesAggressiveConfig()
    {
        // Arrange
        var outputPath = Path.Combine(_tempDirectory, "aggressive-config.json");
        var rootCommand = Program.CreateRootCommand();
        var console = new TestConsole();

        // Act
        var exitCode = await rootCommand.InvokeAsync($"config generate -o {outputPath} -l aggressive", console);

        // Assert
        exitCode.ShouldBe(0);
        File.Exists(outputPath).ShouldBeTrue();

        var json = await File.ReadAllTextAsync(outputPath);
        var settings = JsonSerializer.Deserialize<ObfySettings>(json, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        settings.ShouldNotBeNull();
        // Aggressive level should have most protections enabled
        settings.StringEncryption.Enabled.ShouldBeTrue();
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

    #endregion

    #region Dry Run Tests

    [Fact]
    public async Task Obfuscate_WithDryRun_DoesNotWriteOutput()
    {
        // Arrange
        var assemblyPath = CreateTestAssembly("DryRunTest.dll");
        var outputDir = Path.Combine(_tempDirectory, "dry-run-output");
        Directory.CreateDirectory(outputDir);

        var rootCommand = Program.CreateRootCommand();
        var console = new TestConsole();

        // Act
        var exitCode = await rootCommand.InvokeAsync(
            $"\"{assemblyPath}\" -o \"{outputDir}\" --dry-run --no-logo",
            console);

        exitCode.ShouldBe(0);
        var outputFile = Path.Combine(outputDir, "DryRunTest.dll");
        File.Exists(outputFile).ShouldBeFalse();
    }

    #endregion

    #region Parsing Integration Tests

    [Fact]
    public void Parse_ComplexCommand_AllOptionsCorrect()
    {
        // Arrange
        var assemblyPath = CreateTestAssembly("ComplexTest.dll");
        var outputDir = Path.Combine(_tempDirectory, "complex-output");
        var configPath = Path.Combine(_tempDirectory, "config.json");
        var mapPath = Path.Combine(_tempDirectory, "map.json");
        var reportPath = Path.Combine(_tempDirectory, "report.html");

        // Create a config file
        File.WriteAllText(configPath, "{}");

        var rootCommand = Program.CreateRootCommand();

        // Act
        var parseResult = rootCommand.Parse(
            $"\"{assemblyPath}\" -o \"{outputDir}\" -c \"{configPath}\" " +
            $"--level aggressive --string-encrypt --control-flow --rename " +
            $"--anti-debug --preserve-public --map \"{mapPath}\" --report \"{reportPath}\" " +
            $"--verbose --no-logo");

        // Assert
        parseResult.Errors.ShouldBeEmpty();

        var files = parseResult.GetValueForArgument(Program.InputArgument);
        files.Length.ShouldBe(1);
        files[0].FullName.ShouldBe(assemblyPath);

        parseResult.GetValueForOption(Program.LevelOption).ShouldBe("aggressive");
        parseResult.GetValueForOption(Program.StringEncryptOption).ShouldBeTrue();
        parseResult.GetValueForOption(Program.ControlFlowOption).ShouldBeTrue();
        parseResult.GetValueForOption(Program.RenameOption).ShouldBeTrue();
        parseResult.GetValueForOption(Program.AntiDebugOption).ShouldBeTrue();
        parseResult.GetValueForOption(Program.PreservePublicOption).ShouldBeTrue();
        parseResult.GetValueForOption(Program.VerboseOption).ShouldBeTrue();
        parseResult.GetValueForOption(Program.NoLogoOption).ShouldBeTrue();
    }

    [Fact]
    public void Parse_MergeCommand_AllOptionsCorrect()
    {
        // Arrange
        var assembly1 = CreateTestAssembly("App.dll");
        var assembly2 = CreateTestAssembly("Lib.dll");
        var outputDir = Path.Combine(_tempDirectory, "merge-output");

        var rootCommand = Program.CreateRootCommand();

        // Act
        var parseResult = rootCommand.Parse(
            $"\"{assembly1}\" \"{assembly2}\" -o \"{outputDir}\" --merge --internalize");

        // Assert
        parseResult.Errors.ShouldBeEmpty();

        var files = parseResult.GetValueForArgument(Program.InputArgument);
        files.Length.ShouldBe(2);

        parseResult.GetValueForOption(Program.MergeOption).ShouldBeTrue();
        parseResult.GetValueForOption(Program.InternalizeOption).ShouldBeTrue();
    }

    #endregion

    #region Config File Loading Tests

    [Fact]
    public void Parse_WithConfigFile_ParsesCorrectly()
    {
        // Arrange
        var assemblyPath = CreateTestAssembly("ConfigLoadTest.dll");
        var configPath = Path.Combine(_tempDirectory, "obfy.json");

        var settings = ObfySettings.ForLevel(ObfuscationLevel.Aggressive);
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });
        File.WriteAllText(configPath, json);

        var rootCommand = Program.CreateRootCommand();

        // Act - use argument array to avoid shell parsing issues
        var parseResult = rootCommand.Parse(new[] { assemblyPath, "-c", configPath });

        // Assert
        parseResult.Errors.ShouldBeEmpty();
        var config = parseResult.GetValueForOption(Program.ConfigOption);
        config.ShouldNotBeNull();
        config.Exists.ShouldBeTrue();
    }

    #endregion

    #region Exit Code Tests

    [Fact]
    public async Task Help_ReturnsZeroExitCode()
    {
        // Arrange
        var rootCommand = Program.CreateRootCommand();
        var console = new TestConsole();

        // Act
        var exitCode = await rootCommand.InvokeAsync("--help", console);

        // Assert
        exitCode.ShouldBe(0);
    }

    [Fact]
    public async Task Version_ReturnsZeroExitCode()
    {
        // Arrange
        var rootCommand = Program.CreateRootCommand();
        var console = new TestConsole();

        // Act
        var exitCode = await rootCommand.InvokeAsync("--version", console);

        // Assert
        exitCode.ShouldBe(0);
    }

    [Fact]
    public async Task ConfigGenerateHelp_ReturnsZeroExitCode()
    {
        // Arrange
        var rootCommand = Program.CreateRootCommand();
        var console = new TestConsole();

        // Act
        var exitCode = await rootCommand.InvokeAsync("config generate --help", console);

        // Assert
        exitCode.ShouldBe(0);
    }

    #endregion

    #region Helper Methods

    private string CreateTestAssembly(string name)
    {
        var path = Path.Combine(_tempDirectory, name);

        // Create a minimal valid .NET assembly using dnlib
        var module = new ModuleDefUser(name, Guid.NewGuid(), AssemblyRefUser.CreateMscorlibReferenceCLR40());
        var assembly = new AssemblyDefUser(Path.GetFileNameWithoutExtension(name), new Version(1, 0, 0, 0));
        assembly.Modules.Add(module);

        // Add a simple type
        var typeDef = new TypeDefUser("TestNamespace", "TestClass", module.CorLibTypes.Object.TypeDefOrRef);
        typeDef.Attributes = TypeAttributes.Public | TypeAttributes.Class;
        module.Types.Add(typeDef);

        // Add a simple method with a string
        var method = new MethodDefUser(
            "TestMethod",
            MethodSig.CreateStatic(module.CorLibTypes.Void),
            MethodImplAttributes.IL,
            MethodAttributes.Public | MethodAttributes.Static);

        var body = new CilBody();
        body.Instructions.Add(Instruction.Create(OpCodes.Ldstr, "Hello, World!"));
        body.Instructions.Add(Instruction.Create(OpCodes.Pop));
        body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        method.Body = body;
        typeDef.Methods.Add(method);

        module.Write(path);
        return path;
    }

    #endregion

    #region Real end-to-end obfuscation

    [Fact]
    public async Task Main_RunsRealObfuscationHandler_EndToEnd()
    {
        // Drives the real handler through Program.Main: parse args -> build DI -> run the pipeline.
        var input = CreateTestAssembly("MainE2E.dll");
        var outputDir = Path.Combine(_tempDirectory, "out");

        var exitCode = await Program.Main(new[] { input, "-o", outputDir, "-l", "minimal" });

        exitCode.ShouldBe(0);

        var outputPath = Path.Combine(outputDir, "MainE2E.dll");
        File.Exists(outputPath).ShouldBeTrue();

        // Minimal renames symbols, so the original public type name must be gone from the output.
        using var outModule = ModuleDefMD.Load(outputPath);
        outModule.Types.Any(t => t.Name == "TestClass").ShouldBeFalse();
    }

    #endregion
}
