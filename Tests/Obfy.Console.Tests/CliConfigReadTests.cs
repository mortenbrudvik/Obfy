using Obfy.Core.Models;
using Shouldly;

namespace Obfy.Console.Tests;

/// <summary>
/// The command line reads <c>-c</c> files the way the desktop app does (case-insensitive names) and tells the
/// user about keys it does not know instead of silently running with the defaults.
/// </summary>
public class CliConfigReadTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ObfyCfg_{Guid.NewGuid():N}");

    public CliConfigReadTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    private string WriteConfig(string json, string name = "obfy.json")
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, json);
        return path;
    }

    private static Task<ObfySettings> BuildFromConfigAsync(string path, Action<string>? onWarning = null) =>
        Program.BuildSettingsAsync(
            new FileInfo(path), "standard",
            stringEncrypt: false, controlFlow: false, rename: false, antiDebug: false,
            stripMetadata: false, encryptResources: false, preservePublic: false,
            onWarning: onWarning);

    [Fact]
    public async Task PascalCaseConfig_IsHonored_NotSilentlyDefaulted()
    {
        var path = WriteConfig("""
            {
              "Level": "Custom",
              "StringEncryption": { "Enabled": false },
              "SymbolRenaming": { "Enabled": false, "PreservePublicApi": true },
              "Protection": { "AntiDebug": true }
            }
            """);

        var settings = await BuildFromConfigAsync(path);

        settings.StringEncryption.Enabled.ShouldBeFalse();
        settings.SymbolRenaming.Enabled.ShouldBeFalse();
        settings.SymbolRenaming.PreservePublicApi.ShouldBeTrue();
        settings.Protection.AntiDebug.ShouldBeTrue();
    }

    [Fact]
    public async Task UpperCaseKey_IsHonored()
    {
        var path = WriteConfig("""{ "STRINGENCRYPTION": { "ENABLED": false } }""");

        var settings = await BuildFromConfigAsync(path);

        settings.StringEncryption.Enabled.ShouldBeFalse();
    }

    [Theory]
    [InlineData("BlazorWasm")]
    [InlineData("blazorWasm")]
    [InlineData("blazorwasm")]
    [InlineData(3)]
    public async Task EnumValues_AreReadAsNameInAnyCase_OrAsNumber(object value)
    {
        var literal = value is string text ? $"\"{text}\"" : value.ToString();
        var path = WriteConfig($$"""{ "runtimeProfile": {{literal}}, "stringEncryption": { "algorithm": "xor" } }""");

        var settings = await BuildFromConfigAsync(path);

        settings.RuntimeProfile.ShouldBe(RuntimeProfile.BlazorWasm);
        settings.StringEncryption.Algorithm.ShouldBe(EncryptionAlgorithm.Xor);
    }

    [Fact]
    public async Task ConfigFile_StillForcesCustomLevel()
    {
        var path = WriteConfig("""{ "level": "aggressive", "stringEncryption": { "enabled": false } }""");

        var settings = await BuildFromConfigAsync(path);

        settings.Level.ShouldBe(ObfuscationLevel.Custom);
        settings.StringEncryption.Enabled.ShouldBeFalse();
    }

    [Fact]
    public async Task UnknownTopLevelAndTechniqueKeys_AreReportedAsWarnings_AndTheRestIsStillRead()
    {
        var path = WriteConfig("""
            {
              "stringEncrption": { "enabled": false },
              "controlFlow": { "enabled": false, "intensty": 50 },
              "symbolRenaming": { "enabled": false }
            }
            """);
        var warnings = new List<string>();

        var settings = await BuildFromConfigAsync(path, warnings.Add);

        warnings.Count.ShouldBe(2);
        warnings.ShouldContain(w => w.Contains("'stringEncrption'"));
        warnings.ShouldContain(w => w.Contains("'controlFlow.intensty'"));
        settings.ControlFlow.Enabled.ShouldBeFalse();
        settings.SymbolRenaming.Enabled.ShouldBeFalse();
        settings.StringEncryption.Enabled.ShouldBeTrue("the misspelled section was not applied");
    }

    [Fact]
    public async Task ConfigWithOnlyKnownKeys_ProducesNoWarnings()
    {
        var path = WriteConfig("""
            {
              "$schema": "https://example.invalid/obfy.schema.json",
              "$comment": "notes",
              "level": "custom",
              "stringEncryption": { "enabled": true, "algorithm": "Xor", "minStringLength": 4 },
              "exclusions": { "namespaces": ["My.Ns"] },
              "assemblyMerge": { "enabled": false, "searchDirectories": [] }
            }
            """);
        var warnings = new List<string>();

        await BuildFromConfigAsync(path, warnings.Add);

        warnings.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("minimal")]
    [InlineData("standard")]
    [InlineData("aggressive")]
    public async Task GeneratedConfig_HasNoUnknownKeys(string level)
    {
        var path = Path.Combine(_root, $"gen-{level}.json");
        var exitCode = await CommandLineTestHelpers.InvokeAsync(
            Program.CreateRootCommand(), $"config generate -o \"{path}\" -l {level}");
        exitCode.ShouldBe(0);

        ConfigKeyValidator.FindUnknownKeys(File.ReadAllText(path)).ShouldBeEmpty();
    }

    [Fact]
    public void FindUnknownKeys_ReportsNestedPaths_InTheFileSpelling()
    {
        var unknown = ConfigKeyValidator.FindUnknownKeys("""
            { "Protection": { "AntiDebugg": true, "antiTamper": { "enabld": true } }, "bogus": 1 }
            """);

        unknown.ShouldBe(["Protection.AntiDebugg", "Protection.antiTamper.enabld", "bogus"], ignoreOrder: true);
    }

    [Fact]
    public void FindUnknownKeys_NonObjectRoot_ReturnsNothing()
    {
        ConfigKeyValidator.FindUnknownKeys("[1,2]").ShouldBeEmpty();
        ConfigKeyValidator.FindUnknownKeys("not json").ShouldBeEmpty();
    }

    [Fact]
    public async Task RunWithTypoedConfig_WarnsOnTheConsole_AndStillObfuscates()
    {
        var input = ConsoleTestAssembly.Create(_root, "Typo.dll");
        var config = WriteConfig("""{ "stringEncrption": { "enabled": false }, "symbolRenaming": { "enabled": false } }""");
        var outputDir = Path.Combine(_root, "out");

        using var capture = new AnsiConsoleCapture();
        var exitCode = await CommandLineTestHelpers.InvokeAsync(
            Program.CreateRootCommand(), $"\"{input}\" -c \"{config}\" -o \"{outputDir}\" --no-logo");

        exitCode.ShouldBe(0, "an unknown key is a warning, not a failure");
        capture.Text.ShouldContain("Unknown configuration key 'stringEncrption'");
        File.Exists(Path.Combine(outputDir, "Typo.dll")).ShouldBeTrue();
    }

    [Fact]
    public async Task RunWithPascalCaseConfig_AppliesItToTheOutput()
    {
        // Symbol renaming off in a PascalCase file: the type name must survive. With the old
        // case-sensitive read the file was ignored and the Standard preset renamed it.
        var input = ConsoleTestAssembly.Create(_root, "Pascal.dll", typeName: "KeepMeType");
        var config = WriteConfig("""
            { "Level": "Custom", "SymbolRenaming": { "Enabled": false },
              "StringEncryption": { "Enabled": false } }
            """);
        var outputDir = Path.Combine(_root, "out");

        using var capture = new AnsiConsoleCapture();
        var exitCode = await CommandLineTestHelpers.InvokeAsync(
            Program.CreateRootCommand(), $"\"{input}\" -c \"{config}\" -o \"{outputDir}\" --no-logo");

        exitCode.ShouldBe(0);
        using var module = dnlib.DotNet.ModuleDefMD.Load(Path.Combine(outputDir, "Pascal.dll"));
        module.Types.ShouldContain(t => t.Name == "KeepMeType");
    }
}
