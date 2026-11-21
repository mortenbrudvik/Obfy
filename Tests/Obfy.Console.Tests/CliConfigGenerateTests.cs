using System.Text.Json;
using Obfy.Console.Wizard;
using Shouldly;

namespace Obfy.Console.Tests;

/// <summary>
/// <c>config generate</c> and the wizard write files people keep: never overwrite one unasked, create the folder,
/// write enums as names (the VS Code schema rejects numbers), and refuse a level that is not one.
/// </summary>
public class CliConfigGenerateTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ObfyGen_{Guid.NewGuid():N}");

    public CliConfigGenerateTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    private static (int ExitCode, string Output) Run(string args)
    {
        using var capture = new AnsiConsoleCapture();
        var exitCode = CommandLineTestHelpers.Invoke(Program.CreateRootCommand(), args, out var commandOutput);
        return (exitCode, commandOutput + capture.Text);
    }

    [Fact]
    public void Generate_ExistingFile_IsKeptAndExplainsForce()
    {
        var path = Path.Combine(_root, "obfy.json");
        File.WriteAllText(path, "KEEP ME");

        var (exitCode, output) = Run($"config generate -o \"{path}\"");

        exitCode.ShouldBe(1);
        File.ReadAllText(path).ShouldBe("KEEP ME");
        output.ShouldContain("already exists");
        output.ShouldContain("--force");
    }

    [Fact]
    public void Generate_ExistingFile_WithForce_IsReplaced()
    {
        var path = Path.Combine(_root, "obfy.json");
        File.WriteAllText(path, "OLD");

        var (exitCode, _) = Run($"config generate -o \"{path}\" --force -l minimal");

        exitCode.ShouldBe(0);
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        document.RootElement.GetProperty("level").GetString().ShouldBe("Minimal");
    }

    [Fact]
    public void Generate_MissingDirectory_IsCreated()
    {
        var path = Path.Combine(_root, "not", "yet", "there", "obfy.json");

        var (exitCode, output) = Run($"config generate -o \"{path}\"");

        exitCode.ShouldBe(0, output);
        File.Exists(path).ShouldBeTrue();
    }

    [Fact]
    public void Generate_ParentIsAFile_ReportsAnErrorInsteadOfCrashing()
    {
        var blocker = Path.Combine(_root, "blocker");
        File.WriteAllText(blocker, "a file, not a folder");
        var path = Path.Combine(blocker, "obfy.json");

        var (exitCode, output) = Run($"config generate -o \"{path}\"");

        exitCode.ShouldBe(1);
        output.ShouldNotContain("Unhandled exception");
        output.ShouldNotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("9")]
    [InlineData("-1")]
    [InlineData("42")]
    public void Generate_UndefinedNumericLevel_IsRejected(string level)
    {
        var path = Path.Combine(_root, "numeric.json");

        var (exitCode, output) = Run($"config generate -o \"{path}\" -l {level}");

        exitCode.ShouldBe(1);
        output.ShouldContain($"Unknown obfuscation level '{level}'");
        File.Exists(path).ShouldBeFalse();
    }

    [Fact]
    public async Task Obfuscate_UndefinedNumericLevel_IsRejectedBeforeAnythingIsWritten()
    {
        var input = ConsoleTestAssembly.Create(_root, "Numeric.dll");
        var outputDir = Path.Combine(_root, "out");

        using var capture = new AnsiConsoleCapture();
        var exitCode = await CommandLineTestHelpers.InvokeAsync(
            Program.CreateRootCommand(), $"\"{input}\" -o \"{outputDir}\" -l 9 --no-logo");

        exitCode.ShouldBe(1);
        capture.Text.ShouldContain("Unknown obfuscation level '9'");
        Directory.Exists(outputDir).ShouldBeFalse();
    }

    [Fact]
    public async Task Wizard_WritesEnumsAsNames_AndNoNulls_LikeGenerate()
    {
        var path = Path.Combine(_root, "wizard.json");
        var context = new WizardContext { OutputFile = new FileInfo(path) };

        using var capture = new AnsiConsoleCapture();
        var written = await ConfigurationWizard.GenerateConfigAsync(context);

        written.ShouldBeTrue();
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        root.GetProperty("level").ValueKind.ShouldBe(JsonValueKind.String);
        root.GetProperty("runtimeProfile").ValueKind.ShouldBe(JsonValueKind.String);
        root.GetProperty("stringEncryption").GetProperty("algorithm").ValueKind.ShouldBe(JsonValueKind.String);
        root.GetProperty("controlFlow").GetProperty("mode").ValueKind.ShouldBe(JsonValueKind.String);
        File.ReadAllText(path).ShouldNotContain("null");
    }

    [Fact]
    public async Task Wizard_Output_ReadsBackThroughTheCli()
    {
        var path = Path.Combine(_root, "wizard-roundtrip.json");
        var context = new WizardContext { OutputFile = new FileInfo(path) };
        context.Settings.SymbolRenaming.Enabled = false;
        context.Settings.StringEncryption.Algorithm = Obfy.Core.Models.EncryptionAlgorithm.Xor;

        using var capture = new AnsiConsoleCapture();
        (await ConfigurationWizard.GenerateConfigAsync(context)).ShouldBeTrue();
        var warnings = new List<string>();
        var settings = await Program.BuildSettingsAsync(
            new FileInfo(path), "standard", false, false, false, false, false, false, false, onWarning: warnings.Add);

        warnings.ShouldBeEmpty();
        settings.SymbolRenaming.Enabled.ShouldBeFalse();
        settings.StringEncryption.Algorithm.ShouldBe(Obfy.Core.Models.EncryptionAlgorithm.Xor);
    }
}
