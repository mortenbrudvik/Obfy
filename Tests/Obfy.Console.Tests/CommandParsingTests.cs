using System.CommandLine;
using System.CommandLine.Parsing;
using Obfy.Console;
using Shouldly;

namespace Obfy.Console.Tests;

public class CommandParsingTests : IDisposable
{
    private readonly RootCommand _rootCommand;
    private readonly string _tempDirectory;
    private readonly string _testDll;
    private readonly string _testCs;

    public CommandParsingTests()
    {
        _rootCommand = Program.CreateRootCommand();

        // Create temp directory with test files for parsing
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"ObfyParseTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);

        // Create empty test files so FileInfo parsing works
        _testDll = Path.Combine(_tempDirectory, "input.dll");
        _testCs = Path.Combine(_tempDirectory, "Program.cs");
        File.WriteAllBytes(_testDll, Array.Empty<byte>());
        File.WriteAllText(_testCs, "class Test {}");
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
        catch { }
    }

    #region Basic Input Parsing

    [Fact]
    public void Parse_WithSingleInputFile_Succeeds()
    {
        // Arrange & Act
        var parseResult = _rootCommand.Parse($"\"{_testDll}\"");

        // Assert
        parseResult.Errors.ShouldBeEmpty();
        var files = parseResult.GetValueForArgument(Program.InputArgument);
        files.ShouldNotBeNull();
        files.Length.ShouldBe(1);
        files[0].Name.ShouldBe("input.dll");
    }

    [Fact]
    public void Parse_WithMultipleInputFiles_Succeeds()
    {
        // Arrange - create additional test files
        var file2 = Path.Combine(_tempDirectory, "file2.dll");
        var file3 = Path.Combine(_tempDirectory, "file3.exe");
        File.WriteAllBytes(file2, Array.Empty<byte>());
        File.WriteAllBytes(file3, Array.Empty<byte>());

        // Act
        var parseResult = _rootCommand.Parse($"\"{_testDll}\" \"{file2}\" \"{file3}\"");

        // Assert
        parseResult.Errors.ShouldBeEmpty();
        var files = parseResult.GetValueForArgument(Program.InputArgument);
        files.ShouldNotBeNull();
        files.Length.ShouldBe(3);
    }

    [Fact]
    public void Parse_WithNoInputFiles_ReturnsError()
    {
        // Arrange & Act
        var parseResult = _rootCommand.Parse("");

        // Assert
        parseResult.Errors.ShouldNotBeEmpty();
    }

    [Fact]
    public void Parse_WithCsSourceFile_Succeeds()
    {
        // Arrange & Act
        var parseResult = _rootCommand.Parse($"\"{_testCs}\"");

        // Assert
        parseResult.Errors.ShouldBeEmpty();
        var files = parseResult.GetValueForArgument(Program.InputArgument);
        files.ShouldNotBeNull();
        files[0].Name.ShouldBe("Program.cs");
    }

    #endregion

    #region Output Option

    [Theory]
    [InlineData("-o", "output/")]
    [InlineData("--output", "output/")]
    public void Parse_OutputOption_ParsesCorrectly(string flag, string value)
    {
        // Arrange & Act
        var parseResult = _rootCommand.Parse($"\"{_testDll}\" {flag} {value}");

        // Assert
        parseResult.Errors.ShouldBeEmpty();
        var output = parseResult.GetValueForOption(Program.OutputOption);
        output.ShouldNotBeNull();
        output.Name.ShouldBe("output");
    }

    #endregion

    #region Level Option

    [Theory]
    [InlineData("minimal")]
    [InlineData("standard")]
    [InlineData("aggressive")]
    [InlineData("custom")]
    public void Parse_LevelOption_AcceptsValidLevels(string level)
    {
        // Arrange & Act
        var parseResult = _rootCommand.Parse($"\"{_testDll}\" --level {level}");

        // Assert
        parseResult.Errors.ShouldBeEmpty();
        var parsedLevel = parseResult.GetValueForOption(Program.LevelOption);
        parsedLevel.ShouldBe(level);
    }

    [Theory]
    [InlineData("-l", "minimal")]
    [InlineData("-l", "aggressive")]
    public void Parse_LevelOption_ShortFormWorks(string flag, string level)
    {
        // Arrange & Act
        var parseResult = _rootCommand.Parse($"\"{_testDll}\" {flag} {level}");

        // Assert
        parseResult.Errors.ShouldBeEmpty();
        var parsedLevel = parseResult.GetValueForOption(Program.LevelOption);
        parsedLevel.ShouldBe(level);
    }

    [Fact]
    public void Parse_LevelOption_DefaultsToStandard()
    {
        // Arrange & Act
        var parseResult = _rootCommand.Parse($"\"{_testDll}\"");

        // Assert
        parseResult.Errors.ShouldBeEmpty();
        var level = parseResult.GetValueForOption(Program.LevelOption);
        level.ShouldBe("standard");
    }

    #endregion

    #region Boolean Flags

    [Theory]
    [InlineData("--string-encrypt")]
    [InlineData("--control-flow")]
    [InlineData("--rename")]
    [InlineData("--anti-debug")]
    [InlineData("--anti-dump")]
    [InlineData("--reference-proxy")]
    [InlineData("--encrypt-methods")]
    [InlineData("--encrypt-constants")]
    [InlineData("--no-control-flow")]
    [InlineData("--strip-metadata")]
    [InlineData("--encrypt-resources")]
    [InlineData("--preserve-public")]
    [InlineData("--dry-run")]
    [InlineData("--merge")]
    [InlineData("--verbose")]
    [InlineData("-v")]
    [InlineData("--no-logo")]
    public void Parse_BooleanFlags_ParseCorrectly(string flag)
    {
        // Arrange & Act
        var parseResult = _rootCommand.Parse($"\"{_testDll}\" {flag}");

        // Assert
        parseResult.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void Parse_StringEncryptOption_SetsTrue()
    {
        // Arrange & Act
        var parseResult = _rootCommand.Parse($"\"{_testDll}\" --string-encrypt");

        // Assert
        var value = parseResult.GetValueForOption(Program.StringEncryptOption);
        value.ShouldBeTrue();
    }

    [Fact]
    public void Parse_ControlFlowOption_SetsTrue()
    {
        // Arrange & Act
        var parseResult = _rootCommand.Parse($"\"{_testDll}\" --control-flow");

        // Assert
        var value = parseResult.GetValueForOption(Program.ControlFlowOption);
        value.ShouldBeTrue();
    }

    [Fact]
    public void Parse_RenameOption_SetsTrue()
    {
        // Arrange & Act
        var parseResult = _rootCommand.Parse($"\"{_testDll}\" --rename");

        // Assert
        var value = parseResult.GetValueForOption(Program.RenameOption);
        value.ShouldBeTrue();
    }

    [Fact]
    public void Parse_AntiTamperOption_SetsTrue()
    {
        var parseResult = _rootCommand.Parse($"\"{_testDll}\" --anti-tamper");
        parseResult.GetValueForOption(Program.AntiTamperOption).ShouldBeTrue();
    }

    [Fact]
    public void Parse_AntiDumpOption_SetsTrue()
    {
        var parseResult = _rootCommand.Parse($"\"{_testDll}\" --anti-dump");
        parseResult.GetValueForOption(Program.AntiDumpOption).ShouldBeTrue();
    }

    [Fact]
    public void Parse_ReferenceProxyOption_SetsTrue()
    {
        var parseResult = _rootCommand.Parse($"\"{_testDll}\" --reference-proxy");
        parseResult.GetValueForOption(Program.ReferenceProxyOption).ShouldBeTrue();
    }

    [Fact]
    public void Parse_EncryptMethodsOption_SetsTrue()
    {
        var parseResult = _rootCommand.Parse($"\"{_testDll}\" --encrypt-methods");
        parseResult.GetValueForOption(Program.EncryptMethodsOption).ShouldBeTrue();
    }

    [Fact]
    public void Parse_EncryptConstantsOption_SetsTrue()
    {
        var parseResult = _rootCommand.Parse($"\"{_testDll}\" --encrypt-constants");
        parseResult.GetValueForOption(Program.EncryptConstantsOption).ShouldBeTrue();
    }

    [Fact]
    public void Parse_NoControlFlowOption_SetsTrue()
    {
        var parseResult = _rootCommand.Parse($"\"{_testDll}\" --no-control-flow");
        parseResult.GetValueForOption(Program.NoControlFlowOption).ShouldBeTrue();
    }

    [Fact]
    public void Parse_NoStringEncryptionOption_SetsTrue()
    {
        var parseResult = _rootCommand.Parse($"\"{_testDll}\" --no-string-encryption");
        parseResult.GetValueForOption(Program.NoStringEncryptOption).ShouldBeTrue();
    }

    [Fact]
    public void Parse_AntiDebugOption_SetsTrue()
    {
        // Arrange & Act
        var parseResult = _rootCommand.Parse($"\"{_testDll}\" --anti-debug");

        // Assert
        var value = parseResult.GetValueForOption(Program.AntiDebugOption);
        value.ShouldBeTrue();
    }

    [Fact]
    public void Parse_StripMetadataOption_SetsTrue()
    {
        // Arrange & Act
        var parseResult = _rootCommand.Parse($"\"{_testDll}\" --strip-metadata");

        // Assert
        var value = parseResult.GetValueForOption(Program.StripMetadataOption);
        value.ShouldBeTrue();
    }

    [Fact]
    public void Parse_EncryptResourcesOption_SetsTrue()
    {
        // Arrange & Act
        var parseResult = _rootCommand.Parse($"\"{_testDll}\" --encrypt-resources");

        // Assert
        var value = parseResult.GetValueForOption(Program.EncryptResourcesOption);
        value.ShouldBeTrue();
    }

    [Fact]
    public void Parse_PreservePublicOption_SetsTrue()
    {
        // Arrange & Act
        var parseResult = _rootCommand.Parse($"\"{_testDll}\" --preserve-public");

        // Assert
        var value = parseResult.GetValueForOption(Program.PreservePublicOption);
        value.ShouldBeTrue();
    }

    [Fact]
    public void Parse_DryRunOption_SetsTrue()
    {
        // Arrange & Act
        var parseResult = _rootCommand.Parse($"\"{_testDll}\" --dry-run");

        // Assert
        var value = parseResult.GetValueForOption(Program.DryRunOption);
        value.ShouldBeTrue();
    }

    [Fact]
    public void Parse_MergeOption_SetsTrue()
    {
        // Arrange & Act
        var parseResult = _rootCommand.Parse($"\"{_testDll}\" --merge");

        // Assert
        var value = parseResult.GetValueForOption(Program.MergeOption);
        value.ShouldBeTrue();
    }

    [Fact]
    public void Parse_VerboseOption_SetsTrue()
    {
        // Arrange & Act
        var parseResult = _rootCommand.Parse($"\"{_testDll}\" --verbose");

        // Assert
        var value = parseResult.GetValueForOption(Program.VerboseOption);
        value.ShouldBeTrue();
    }

    [Fact]
    public void Parse_VerboseShortOption_SetsTrue()
    {
        // Arrange & Act
        var parseResult = _rootCommand.Parse($"\"{_testDll}\" -v");

        // Assert
        var value = parseResult.GetValueForOption(Program.VerboseOption);
        value.ShouldBeTrue();
    }

    [Fact]
    public void Parse_NoLogoOption_SetsTrue()
    {
        // Arrange & Act
        var parseResult = _rootCommand.Parse($"\"{_testDll}\" --no-logo");

        // Assert
        var value = parseResult.GetValueForOption(Program.NoLogoOption);
        value.ShouldBeTrue();
    }

    [Fact]
    public void Parse_InternalizeOption_DefaultsToTrue()
    {
        // Arrange & Act
        var parseResult = _rootCommand.Parse($"\"{_testDll}\"");

        // Assert
        var value = parseResult.GetValueForOption(Program.InternalizeOption);
        value.ShouldBeTrue();
    }

    #endregion

    #region Config Option

    [Fact]
    public void Parse_ConfigOption_AcceptsJsonFile()
    {
        // Arrange
        var configPath = Path.Combine(_tempDirectory, "obfy.json");
        File.WriteAllText(configPath, "{}");

        // Act
        var parseResult = _rootCommand.Parse($"\"{_testDll}\" --config \"{configPath}\"");

        // Assert
        parseResult.Errors.ShouldBeEmpty();
        var config = parseResult.GetValueForOption(Program.ConfigOption);
        config.ShouldNotBeNull();
        config.Name.ShouldBe("obfy.json");
    }

    [Theory]
    [InlineData("-c")]
    [InlineData("--config")]
    public void Parse_ConfigOption_BothAliasesWork(string flag)
    {
        // Arrange
        var configPath = Path.Combine(_tempDirectory, "config.json");
        File.WriteAllText(configPath, "{}");

        // Act
        var parseResult = _rootCommand.Parse($"\"{_testDll}\" {flag} \"{configPath}\"");

        // Assert
        parseResult.Errors.ShouldBeEmpty();
        var config = parseResult.GetValueForOption(Program.ConfigOption);
        config.ShouldNotBeNull();
    }

    #endregion

    #region Map Option

    [Fact]
    public void Parse_MapOption_AcceptsFile()
    {
        // Arrange
        var mapPath = Path.Combine(_tempDirectory, "symbols.json");

        // Act
        var parseResult = _rootCommand.Parse($"\"{_testDll}\" --map \"{mapPath}\"");

        // Assert
        parseResult.Errors.ShouldBeEmpty();
        var map = parseResult.GetValueForOption(Program.MapOption);
        map.ShouldNotBeNull();
        map.Name.ShouldBe("symbols.json");
    }

    #endregion

    #region Report Option

    [Theory]
    [InlineData("report.html")]
    [InlineData("report.json")]
    public void Parse_ReportOption_AcceptsValidFormats(string fileName)
    {
        // Arrange
        var reportPath = Path.Combine(_tempDirectory, fileName);

        // Act
        var parseResult = _rootCommand.Parse($"\"{_testDll}\" --report \"{reportPath}\"");

        // Assert
        parseResult.Errors.ShouldBeEmpty();
        var report = parseResult.GetValueForOption(Program.ReportOption);
        report.ShouldNotBeNull();
        report.Name.ShouldBe(fileName);
    }

    #endregion

    #region Subcommands

    [Fact]
    public void Parse_ConfigGenerate_ParsesCorrectly()
    {
        // Arrange & Act
        var parseResult = _rootCommand.Parse("config generate");

        // Assert
        parseResult.Errors.ShouldBeEmpty();
        parseResult.CommandResult.Command.Name.ShouldBe("generate");
    }

    [Fact]
    public void Parse_ConfigGenerate_WithOutput_ParsesCorrectly()
    {
        // Arrange & Act
        var parseResult = _rootCommand.Parse("config generate -o myconfig.json");

        // Assert
        parseResult.Errors.ShouldBeEmpty();
        parseResult.CommandResult.Command.Name.ShouldBe("generate");
    }

    [Fact]
    public void Parse_ConfigGenerate_WithLevel_ParsesCorrectly()
    {
        // Arrange & Act
        var parseResult = _rootCommand.Parse("config generate --level aggressive");

        // Assert
        parseResult.Errors.ShouldBeEmpty();
        parseResult.CommandResult.Command.Name.ShouldBe("generate");
    }

    [Fact]
    public void Parse_ConfigWizard_ParsesCorrectly()
    {
        // Arrange & Act
        var parseResult = _rootCommand.Parse("config wizard");

        // Assert
        parseResult.Errors.ShouldBeEmpty();
        parseResult.CommandResult.Command.Name.ShouldBe("wizard");
    }

    [Fact]
    public void Parse_ConfigWizardQuickMode_ParsesCorrectly()
    {
        // Arrange & Act
        var parseResult = _rootCommand.Parse("config wizard --quick");

        // Assert
        parseResult.Errors.ShouldBeEmpty();
        parseResult.CommandResult.Command.Name.ShouldBe("wizard");
    }

    [Fact]
    public void Parse_ConfigWizard_WithOutput_ParsesCorrectly()
    {
        // Arrange & Act
        var parseResult = _rootCommand.Parse("config wizard -o custom.json");

        // Assert
        parseResult.Errors.ShouldBeEmpty();
        parseResult.CommandResult.Command.Name.ShouldBe("wizard");
    }

    #endregion

    #region Multiple Options Combined

    [Fact]
    public void Parse_MultipleOptionsAndFlags_ParsesCorrectly()
    {
        // Arrange & Act
        var parseResult = _rootCommand.Parse(
            $"\"{_testDll}\" -o output/ --level aggressive --string-encrypt --control-flow --rename --verbose");

        // Assert
        parseResult.Errors.ShouldBeEmpty();
        parseResult.GetValueForOption(Program.LevelOption).ShouldBe("aggressive");
        parseResult.GetValueForOption(Program.StringEncryptOption).ShouldBeTrue();
        parseResult.GetValueForOption(Program.ControlFlowOption).ShouldBeTrue();
        parseResult.GetValueForOption(Program.RenameOption).ShouldBeTrue();
        parseResult.GetValueForOption(Program.VerboseOption).ShouldBeTrue();
    }

    [Fact]
    public void Parse_AllProtectionFlags_ParsesCorrectly()
    {
        // Arrange & Act
        var parseResult = _rootCommand.Parse(
            $"\"{_testDll}\" --string-encrypt --control-flow --rename --anti-debug --anti-dump --reference-proxy --encrypt-methods --encrypt-constants --no-control-flow --strip-metadata --encrypt-resources --preserve-public");

        // Assert
        parseResult.Errors.ShouldBeEmpty();
        parseResult.GetValueForOption(Program.StringEncryptOption).ShouldBeTrue();
        parseResult.GetValueForOption(Program.ControlFlowOption).ShouldBeTrue();
        parseResult.GetValueForOption(Program.RenameOption).ShouldBeTrue();
        parseResult.GetValueForOption(Program.AntiDebugOption).ShouldBeTrue();
        parseResult.GetValueForOption(Program.AntiDumpOption).ShouldBeTrue();
        parseResult.GetValueForOption(Program.ReferenceProxyOption).ShouldBeTrue();
        parseResult.GetValueForOption(Program.EncryptMethodsOption).ShouldBeTrue();
        parseResult.GetValueForOption(Program.EncryptConstantsOption).ShouldBeTrue();
        parseResult.GetValueForOption(Program.NoControlFlowOption).ShouldBeTrue();
        parseResult.GetValueForOption(Program.StripMetadataOption).ShouldBeTrue();
        parseResult.GetValueForOption(Program.EncryptResourcesOption).ShouldBeTrue();
        parseResult.GetValueForOption(Program.PreservePublicOption).ShouldBeTrue();
    }

    [Fact]
    public void Parse_WatermarkIdOption_SetsValue()
    {
        var parseResult = _rootCommand.Parse($"\"{_testDll}\" --watermark-id customer-42");
        parseResult.Errors.ShouldBeEmpty();
        parseResult.GetValueForOption(Program.WatermarkIdOption).ShouldBe("customer-42");
    }

    [Fact]
    public void Parse_MergeWithMultipleInputs_ParsesCorrectly()
    {
        // Arrange - create additional test files
        var lib1 = Path.Combine(_tempDirectory, "lib1.dll");
        var lib2 = Path.Combine(_tempDirectory, "lib2.dll");
        File.WriteAllBytes(lib1, Array.Empty<byte>());
        File.WriteAllBytes(lib2, Array.Empty<byte>());

        // Act
        var parseResult = _rootCommand.Parse($"\"{_testDll}\" \"{lib1}\" \"{lib2}\" --merge -o output/");

        // Assert
        parseResult.Errors.ShouldBeEmpty();
        var files = parseResult.GetValueForArgument(Program.InputArgument);
        files.Length.ShouldBe(3);
        parseResult.GetValueForOption(Program.MergeOption).ShouldBeTrue();
    }

    #endregion
}
