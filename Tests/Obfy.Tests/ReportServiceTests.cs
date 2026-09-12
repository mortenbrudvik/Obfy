using Microsoft.Extensions.Logging;
using Moq;
using Obfy.Core.Models;
using Obfy.Core.Services.Reporting;
using Shouldly;

namespace Obfy.Tests;

/// <summary>
/// Tests for report generation services.
/// </summary>
public class ReportServiceTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly ReportService _reportService;
    private readonly HtmlReportGenerator _htmlGenerator;
    private readonly JsonReportGenerator _jsonGenerator;

    public ReportServiceTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"ObfyReportTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);

        var reportLogger = new Mock<ILogger<ReportService>>();
        var htmlLogger = new Mock<ILogger<HtmlReportGenerator>>();
        var jsonLogger = new Mock<ILogger<JsonReportGenerator>>();

        _htmlGenerator = new HtmlReportGenerator(htmlLogger.Object);
        _jsonGenerator = new JsonReportGenerator(jsonLogger.Object);
        _reportService = new ReportService(reportLogger.Object, _htmlGenerator, _jsonGenerator);
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

    #region BuildReport Tests

    [Fact]
    public void BuildReport_CreatesValidReport()
    {
        // Arrange
        var result = CreateSuccessfulResult();
        var settings = new ObfySettings { Level = ObfuscationLevel.Standard };

        // Act
        var report = _reportService.BuildReport(result, settings);

        // Assert
        report.ShouldNotBeNull();
        report.Metadata.ShouldNotBeNull();
        report.FileInfo.ShouldNotBeNull();
        report.Statistics.ShouldNotBeNull();
        report.SymbolSummary.ShouldNotBeNull();
        report.SettingsUsed.ShouldNotBeNull();
        report.Warnings.ShouldNotBeNull();
        report.ProcessingTimes.ShouldNotBeNull();
    }

    [Fact]
    public void BuildReport_IncludesCorrectMetadata()
    {
        // Arrange
        var result = CreateSuccessfulResult();
        var settings = new ObfySettings { Level = ObfuscationLevel.Aggressive };

        // Act
        var report = _reportService.BuildReport(result, settings);

        // Assert
        report.Metadata.Level.ShouldBe(ObfuscationLevel.Aggressive);
        report.Metadata.GeneratedAt.ShouldBeInRange(DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(1));
        report.Metadata.ObfyVersion.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public void BuildReport_IncludesStatistics()
    {
        // Arrange
        var result = CreateSuccessfulResult();
        result = ObfuscationResult.Successful(
            new ObfuscationStatistics
            {
                StringsEncrypted = 10,
                TypesRenamed = 5,
                MethodsRenamed = 20,
                FieldsRenamed = 8
            });
        var settings = new ObfySettings();

        // Act
        var report = _reportService.BuildReport(result, settings);

        // Assert
        report.Statistics.StringsEncrypted.ShouldBe(10);
        report.Statistics.TypesRenamed.ShouldBe(5);
        report.Statistics.MethodsRenamed.ShouldBe(20);
        report.Statistics.FieldsRenamed.ShouldBe(8);
    }

    [Fact]
    public void BuildReport_IncludesWarningsForUnusedSettings()
    {
        // Arrange
        var result = CreateSuccessfulResult();
        var settings = new ObfySettings
        {
            StringEncryption = { Enabled = true },
            ControlFlow = { Enabled = true }
        };
        // Statistics show zero for both

        // Act
        var report = _reportService.BuildReport(result, settings);

        // Assert
        report.Warnings.ShouldNotBeEmpty();
        report.Warnings.ShouldContain(w => w.Category == WarningCategory.UnusedSetting &&
                                          w.RelatedItem == "StringEncryption");
        report.Warnings.ShouldContain(w => w.Category == WarningCategory.UnusedSetting &&
                                          w.RelatedItem == "ControlFlow");
    }

    [Fact]
    public void BuildReport_IncludesUnusedSettingForMethodEncryption()
    {
        var result = CreateSuccessfulResult();
        var settings = new ObfySettings { Protection = { MethodEncryption = true } };

        var report = _reportService.BuildReport(result, settings);

        report.Warnings.ShouldContain(w => w.Category == WarningCategory.UnusedSetting &&
                                          w.RelatedItem == "MethodEncryption");
    }

    [Fact]
    public void BuildReport_IncludesUnusedSettingForDependencyEmbedding()
    {
        var result = CreateSuccessfulResult();
        var settings = new ObfySettings { DependencyEmbedding = { Enabled = true } };

        var report = _reportService.BuildReport(result, settings);

        report.Warnings.ShouldContain(w => w.Category == WarningCategory.UnusedSetting &&
                                          w.RelatedItem == "DependencyEmbedding");
    }

    [Fact]
    public void BuildReport_IncludesRuntimeWarningsFromResult()
    {
        var result = ObfuscationResult.Successful(
            new ObfuscationStatistics { StringsEncrypted = 1 },
            warnings: new List<string>
            {
                "Anti-tamper: the integrity check verifies the assembly file on disk and is skipped for single-file / self-contained deployments (Assembly.Location is empty). Ship a file-based deployment for tamper protection to take effect."
            });
        var settings = new ObfySettings();

        var report = _reportService.BuildReport(result, settings);

        report.Warnings.ShouldContain(w =>
            w.Category == WarningCategory.ProtectionIneffective &&
            w.Message.Contains("Anti-tamper"));
    }

    [Fact]
    public void BuildReport_IncludesPublicApiWarning()
    {
        // Arrange
        var result = ObfuscationResult.Successful(
            new ObfuscationStatistics { TypesRenamed = 5 });
        var settings = new ObfySettings
        {
            SymbolRenaming =
            {
                Enabled = true,
                PreservePublicApi = false
            }
        };

        // Act
        var report = _reportService.BuildReport(result, settings);

        // Assert
        report.Warnings.ShouldContain(w => w.Category == WarningCategory.PublicApiChange);
    }

    [Fact]
    public void BuildReport_CalculatesSymbolSummary()
    {
        // Arrange
        var symbolMap = new Dictionary<string, string>
        {
            { "Type:MyClass", "a" },
            { "Type:OtherClass", "b" },
            { "Method:MyClass::Foo", "c" },
            { "Field:MyClass::_value", "d" },
            { "Property:MyClass::Name", "e" }
        };
        var result = ObfuscationResult.Successful(
            new ObfuscationStatistics(),
            symbolMap: symbolMap);
        var settings = new ObfySettings();

        // Act
        var report = _reportService.BuildReport(result, settings);

        // Assert
        report.SymbolSummary.TotalSymbolsRenamed.ShouldBe(5);
        report.SymbolSummary.TypesRenamed.ShouldBe(2);
        report.SymbolSummary.MethodsRenamed.ShouldBe(1);
        report.SymbolSummary.FieldsRenamed.ShouldBe(1);
        report.SymbolSummary.PropertiesRenamed.ShouldBe(1);
    }

    [Fact]
    public void BuildReport_IncludesProcessingTimes()
    {
        // Arrange
        var processingTimes = new List<ProcessingTimeEntry>
        {
            new() { ObfuscatorName = "StringEncryption", Duration = TimeSpan.FromMilliseconds(100), TransformationsApplied = 5 },
            new() { ObfuscatorName = "SymbolRenaming", Duration = TimeSpan.FromMilliseconds(200), TransformationsApplied = 10 }
        };
        var result = ObfuscationResult.Successful(
            new ObfuscationStatistics(),
            processingTimes: processingTimes);
        var settings = new ObfySettings();

        // Act
        var report = _reportService.BuildReport(result, settings);

        // Assert
        report.ProcessingTimes.Count.ShouldBe(2);
        report.ProcessingTimes[0].ObfuscatorName.ShouldBe("StringEncryption");
        report.ProcessingTimes[1].ObfuscatorName.ShouldBe("SymbolRenaming");
    }

    [Fact]
    public void BuildReport_IncludesSkippedItemWarnings()
    {
        // Arrange
        var skippedItems = new List<SkippedItem>
        {
            new() { Reason = SkipReason.StringTooShort, ItemType = SkippedItemType.String, ItemName = "\"ab\"" },
            new() { Reason = SkipReason.StringTooShort, ItemType = SkippedItemType.String, ItemName = "\"cd\"" },
            new() { Reason = SkipReason.PreservedPublicApi, ItemType = SkippedItemType.Method, ItemName = "Main" }
        };
        var result = ObfuscationResult.Successful(
            new ObfuscationStatistics(),
            skippedItems: skippedItems);
        var settings = new ObfySettings();

        // Act
        var report = _reportService.BuildReport(result, settings);

        // Assert
        report.Warnings.ShouldContain(w => w.Category == WarningCategory.SkippedItem);
    }

    #endregion

    #region JsonReportGenerator Tests

    [Fact]
    public async Task JsonGenerator_ProducesValidJson()
    {
        // Arrange
        var report = CreateSampleReport();
        var outputPath = Path.Combine(_tempDirectory, "report.json");

        // Act
        await _jsonGenerator.GenerateAsync(report, outputPath);

        // Assert
        File.Exists(outputPath).ShouldBeTrue();
        var content = await File.ReadAllTextAsync(outputPath);
        content.ShouldNotBeNullOrEmpty();
        content.ShouldContain("\"metadata\"");
        content.ShouldContain("\"statistics\"");
    }

    [Fact]
    public async Task JsonGenerator_GeneratesToString()
    {
        // Arrange
        var report = CreateSampleReport();

        // Act
        var json = await _jsonGenerator.GenerateToStringAsync(report);

        // Assert
        json.ShouldNotBeNullOrEmpty();
        json.ShouldContain("\"metadata\"");
        json.ShouldContain("\"fileInfo\"");
    }

    [Fact]
    public void JsonGenerator_HasCorrectExtension()
    {
        _jsonGenerator.FileExtension.ShouldBe(".json");
    }

    #endregion

    #region HtmlReportGenerator Tests

    [Fact]
    public async Task HtmlGenerator_ProducesValidHtml()
    {
        // Arrange
        var report = CreateSampleReport();
        var outputPath = Path.Combine(_tempDirectory, "report.html");

        // Act
        await _htmlGenerator.GenerateAsync(report, outputPath);

        // Assert
        File.Exists(outputPath).ShouldBeTrue();
        var content = await File.ReadAllTextAsync(outputPath);
        content.ShouldContain("<!DOCTYPE html>");
        content.ShouldContain("<html");
        content.ShouldContain("</html>");
    }

    [Fact]
    public async Task HtmlGenerator_ContainsRequiredSections()
    {
        // Arrange
        var report = CreateSampleReport();

        // Act
        var html = await _htmlGenerator.GenerateToStringAsync(report);

        // Assert
        html.ShouldContain("Summary");
        html.ShouldContain("File Information");
        html.ShouldContain("Transformation Statistics");
        html.ShouldContain("Symbol Renaming Summary");
        html.ShouldContain("Settings Used");
        html.ShouldContain("Processing Times");
        html.ShouldContain("Warnings");
    }

    [Fact]
    public async Task HtmlGenerator_IncludesStyles()
    {
        // Arrange
        var report = CreateSampleReport();

        // Act
        var html = await _htmlGenerator.GenerateToStringAsync(report);

        // Assert
        html.ShouldContain("<style>");
        html.ShouldContain("</style>");
    }

    [Fact]
    public void HtmlGenerator_HasCorrectExtension()
    {
        _htmlGenerator.FileExtension.ShouldBe(".html");
    }

    [Fact]
    public async Task HtmlGenerator_CreatesOutputDirectory()
    {
        // Arrange
        var report = CreateSampleReport();
        var outputPath = Path.Combine(_tempDirectory, "subdir", "nested", "report.html");

        // Act
        await _htmlGenerator.GenerateAsync(report, outputPath);

        // Assert
        File.Exists(outputPath).ShouldBeTrue();
    }

    #endregion

    #region ReportService GenerateReportAsync Tests

    [Fact]
    public async Task GenerateReportAsync_GeneratesHtml()
    {
        // Arrange
        var report = CreateSampleReport();
        var outputPath = Path.Combine(_tempDirectory, "output.html");

        // Act
        await _reportService.GenerateReportAsync(report, outputPath, ReportFormat.Html);

        // Assert
        File.Exists(outputPath).ShouldBeTrue();
        var content = await File.ReadAllTextAsync(outputPath);
        content.ShouldContain("<!DOCTYPE html>");
    }

    [Fact]
    public async Task GenerateReportAsync_GeneratesJson()
    {
        // Arrange
        var report = CreateSampleReport();
        var outputPath = Path.Combine(_tempDirectory, "output.json");

        // Act
        await _reportService.GenerateReportAsync(report, outputPath, ReportFormat.Json);

        // Assert
        File.Exists(outputPath).ShouldBeTrue();
        var content = await File.ReadAllTextAsync(outputPath);
        content.ShouldContain("\"metadata\"");
    }

    [Fact]
    public void GetFileExtension_ReturnsCorrectExtensions()
    {
        _reportService.GetFileExtension(ReportFormat.Html).ShouldBe(".html");
        _reportService.GetFileExtension(ReportFormat.Json).ShouldBe(".json");
    }

    #endregion

    #region Helper Methods

    private static ObfuscationResult CreateSuccessfulResult()
    {
        return ObfuscationResult.Successful(
            new ObfuscationStatistics(),
            inputPath: "/input/test.dll",
            outputPath: "/output/test.dll",
            elapsedTime: TimeSpan.FromSeconds(1));
    }

    private static ObfuscationReport CreateSampleReport()
    {
        return new ObfuscationReport
        {
            Metadata = new ReportMetadata
            {
                ObfyVersion = "1.0.0",
                GeneratedAt = DateTime.UtcNow,
                Level = ObfuscationLevel.Standard,
                TotalElapsedTime = TimeSpan.FromSeconds(1.5)
            },
            FileInfo = new FileInfoSection
            {
                InputPath = "/input/test.dll",
                OutputPath = "/output/test.dll",
                InputSizeBytes = 10240,
                OutputSizeBytes = 12288
            },
            Statistics = new ObfuscationStatistics
            {
                StringsEncrypted = 10,
                TypesRenamed = 5,
                MethodsRenamed = 20,
                FieldsRenamed = 8
            },
            SymbolSummary = new SymbolMapSummary
            {
                TotalSymbolsRenamed = 33,
                TypesRenamed = 5,
                MethodsRenamed = 20,
                FieldsRenamed = 8
            },
            SettingsUsed = new SettingsSummary
            {
                StringEncryptionEnabled = true,
                SymbolRenamingEnabled = true,
                ControlFlowEnabled = false
            },
            Warnings = new List<ReportWarning>
            {
                new()
                {
                    Severity = WarningSeverity.Info,
                    Category = WarningCategory.SkippedItem,
                    Message = "5 strings were too short to encrypt"
                }
            },
            ProcessingTimes = new List<ProcessingTimeEntry>
            {
                new() { ObfuscatorName = "StringEncryption", Duration = TimeSpan.FromMilliseconds(500) },
                new() { ObfuscatorName = "SymbolRenaming", Duration = TimeSpan.FromMilliseconds(800) }
            }
        };
    }

    #endregion
}
