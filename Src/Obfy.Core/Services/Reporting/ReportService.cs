using System.Reflection;
using Microsoft.Extensions.Logging;
using Obfy.Core.Models;
using Obfy.Core.Pipeline;

namespace Obfy.Core.Services.Reporting;

/// <summary>
/// Default implementation of the report service.
/// </summary>
public class ReportService : IReportService
{
    private readonly ILogger<ReportService> _logger;
    private readonly HtmlReportGenerator _htmlGenerator;
    private readonly JsonReportGenerator _jsonGenerator;

    /// <summary>
    /// Creates a new report service.
    /// </summary>
    public ReportService(
        ILogger<ReportService> logger,
        HtmlReportGenerator htmlGenerator,
        JsonReportGenerator jsonGenerator)
    {
        _logger = logger;
        _htmlGenerator = htmlGenerator;
        _jsonGenerator = jsonGenerator;
    }

    /// <inheritdoc/>
    public ObfuscationReport BuildReport(
        PipelineContext context,
        ObfuscationResult result,
        ObfySettings settings)
    {
        _logger.LogDebug("Building obfuscation report");

        var report = new ObfuscationReport
        {
            Metadata = BuildMetadata(result, settings),
            FileInfo = BuildFileInfo(context),
            Statistics = result.Statistics,
            SymbolSummary = BuildSymbolSummary(context, settings),
            SettingsUsed = BuildSettingsSummary(settings),
            Warnings = BuildWarnings(context, result, settings),
            ProcessingTimes = context.ProcessingTimes.ToList()
        };

        _logger.LogDebug("Report built with {WarningCount} warnings", report.Warnings.Count);

        return report;
    }

    /// <inheritdoc/>
    public ObfuscationReport BuildReport(
        ObfuscationResult result,
        ObfySettings settings)
    {
        _logger.LogDebug("Building obfuscation report from result");

        var report = new ObfuscationReport
        {
            Metadata = BuildMetadata(result, settings),
            FileInfo = BuildFileInfoFromResult(result),
            Statistics = result.Statistics,
            SymbolSummary = BuildSymbolSummaryFromResult(result, settings),
            SettingsUsed = BuildSettingsSummary(settings),
            Warnings = BuildWarningsFromResult(result, settings),
            ProcessingTimes = result.ProcessingTimes.ToList()
        };

        _logger.LogDebug("Report built with {WarningCount} warnings", report.Warnings.Count);

        return report;
    }

    /// <inheritdoc/>
    public async Task GenerateReportAsync(
        ObfuscationReport report,
        string outputPath,
        ReportFormat format,
        CancellationToken cancellationToken = default)
    {
        IReportGenerator generator = format switch
        {
            ReportFormat.Html => _htmlGenerator,
            ReportFormat.Json => _jsonGenerator,
            _ => throw new ArgumentException($"Unsupported report format: {format}", nameof(format))
        };

        await generator.GenerateAsync(report, outputPath, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public string GetFileExtension(ReportFormat format) => format switch
    {
        ReportFormat.Html => ".html",
        ReportFormat.Json => ".json",
        _ => ".txt"
    };

    private static ReportMetadata BuildMetadata(ObfuscationResult result, ObfySettings settings)
    {
        return new ReportMetadata
        {
            ObfyVersion = GetObfyVersion(),
            GeneratedAt = DateTime.UtcNow,
            Level = settings.Level,
            TotalElapsedTime = result.ElapsedTime
        };
    }

    private static string GetObfyVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        return version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "1.0.0";
    }

    private FileInfoSection BuildFileInfo(PipelineContext context)
    {
        var inputPath = context.InputPath ?? string.Empty;
        var outputPath = context.OutputPath ?? string.Empty;

        return new FileInfoSection
        {
            InputPath = inputPath,
            OutputPath = outputPath,
            InputSizeBytes = TryGetFileSize(inputPath),
            OutputSizeBytes = TryGetFileSize(outputPath)
        };
    }

    /// <summary>
    /// Returns the size of the file at <paramref name="path"/>, or 0 if it does not exist or cannot be
    /// read. File-access failures are logged at debug rather than silently swallowed.
    /// </summary>
    private long TryGetFileSize(string path)
    {
        try
        {
            return File.Exists(path) ? new FileInfo(path).Length : 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _logger.LogDebug(ex, "Could not read size of {Path} for the report", path);
            return 0;
        }
    }

    private static SymbolMapSummary BuildSymbolSummary(PipelineContext context, ObfySettings settings)
    {
        var typeCount = 0;
        var methodCount = 0;
        var fieldCount = 0;
        var propertyCount = 0;
        var parameterCount = 0;

        foreach (var key in context.SymbolMap.Keys)
        {
            if (key.StartsWith("Type:"))
                typeCount++;
            else if (key.StartsWith("Method:"))
                methodCount++;
            else if (key.StartsWith("Field:"))
                fieldCount++;
            else if (key.StartsWith("Property:"))
                propertyCount++;
            else if (key.StartsWith("Parameter:"))
                parameterCount++;
        }

        return new SymbolMapSummary
        {
            TotalSymbolsRenamed = context.SymbolMap.Count,
            TypesRenamed = typeCount,
            MethodsRenamed = methodCount,
            FieldsRenamed = fieldCount,
            PropertiesRenamed = propertyCount,
            ParametersRenamed = parameterCount,
            NamingModeUsed = settings.SymbolRenaming.Mode
        };
    }

    private static SettingsSummary BuildSettingsSummary(ObfySettings settings)
    {
        return new SettingsSummary
        {
            StringEncryptionEnabled = settings.StringEncryption.Enabled,
            ConstantEncryptionEnabled = settings.ConstantEncryption.Enabled,
            ResourceEncryptionEnabled = settings.ResourceEncryption.Enabled,
            ControlFlowEnabled = settings.ControlFlow.Enabled,
            SymbolRenamingEnabled = settings.SymbolRenaming.Enabled,
            AntiDebugEnabled = settings.Protection.AntiDebug,
            MetadataRemovalEnabled = settings.Metadata.RemoveDebugInfo,
            PreservePublicApi = settings.SymbolRenaming.PreservePublicApi
        };
    }

    private static List<ReportWarning> BuildWarnings(
        PipelineContext context,
        ObfuscationResult result,
        ObfySettings settings)
    {
        var warnings = new List<ReportWarning>();

        // Check for skipped items
        var skippedItems = context.SkippedItems;
        if (skippedItems.Count > 0)
        {
            var groupedSkips = skippedItems
                .GroupBy(s => s.Reason)
                .OrderByDescending(g => g.Count());

            foreach (var group in groupedSkips.Take(5))
            {
                var examples = string.Join(", ", group.Take(3).Select(i => i.ItemName));
                if (group.Count() > 3)
                {
                    examples += $", ... (+{group.Count() - 3} more)";
                }

                warnings.Add(new ReportWarning
                {
                    Severity = WarningSeverity.Info,
                    Category = WarningCategory.SkippedItem,
                    Message = $"{group.Count()} items skipped: {FormatSkipReason(group.Key)}",
                    RelatedItem = group.Key.ToString(),
                    Details = examples
                });
            }
        }

        AddRuntimeWarnings(warnings, context.Warnings);

        // Check for unused settings
        if (settings.StringEncryption.Enabled && result.Statistics.StringsEncrypted == 0)
        {
            warnings.Add(new ReportWarning
            {
                Severity = WarningSeverity.Warning,
                Category = WarningCategory.UnusedSetting,
                Message = "String encryption was enabled but no strings were encrypted",
                RelatedItem = "StringEncryption",
                Details = "The assembly may not contain string literals, or they may all be below the minimum length threshold."
            });
        }

        if (settings.ConstantEncryption.Enabled && result.Statistics.ConstantsEncrypted == 0)
        {
            warnings.Add(new ReportWarning
            {
                Severity = WarningSeverity.Warning,
                Category = WarningCategory.UnusedSetting,
                Message = "Constant encryption was enabled but no constants were encrypted",
                RelatedItem = "ConstantEncryption",
                Details = "Constants may be below the threshold or the assembly may not contain numeric literals."
            });
        }

        if (settings.ResourceEncryption.Enabled && result.Statistics.ResourcesEncrypted == 0)
        {
            warnings.Add(new ReportWarning
            {
                Severity = WarningSeverity.Warning,
                Category = WarningCategory.UnusedSetting,
                Message = "Resource encryption was enabled but no resources were encrypted",
                RelatedItem = "ResourceEncryption",
                Details = "The assembly may not contain embedded resources, or they may all be excluded by patterns."
            });
        }

        if (settings.ControlFlow.Enabled && result.Statistics.MethodsControlFlowObfuscated == 0)
        {
            warnings.Add(new ReportWarning
            {
                Severity = WarningSeverity.Warning,
                Category = WarningCategory.UnusedSetting,
                Message = "Control flow obfuscation was enabled but no methods were obfuscated",
                RelatedItem = "ControlFlow",
                Details = "Methods may be too simple, contain exception handlers, or be excluded."
            });
        }

        if (settings.SymbolRenaming.Enabled && result.Statistics.TypesRenamed == 0 &&
            result.Statistics.MethodsRenamed == 0 && result.Statistics.FieldsRenamed == 0)
        {
            warnings.Add(new ReportWarning
            {
                Severity = WarningSeverity.Warning,
                Category = WarningCategory.UnusedSetting,
                Message = "Symbol renaming was enabled but no symbols were renamed",
                RelatedItem = "SymbolRenaming",
                Details = "All symbols may be preserved due to exclusion rules or public API preservation."
            });
        }

        // Check for public API changes
        if (settings.SymbolRenaming.Enabled && !settings.SymbolRenaming.PreservePublicApi &&
            result.Statistics.TypesRenamed > 0)
        {
            warnings.Add(new ReportWarning
            {
                Severity = WarningSeverity.Important,
                Category = WarningCategory.PublicApiChange,
                Message = "Public API names may have been changed",
                RelatedItem = "PreservePublicApi",
                Details = "If this assembly is consumed by other projects, consider enabling 'Preserve Public API' to maintain compatibility."
            });
        }

        return warnings;
    }

    private static void AddRuntimeWarnings(List<ReportWarning> warnings, IEnumerable<string> runtimeWarnings)
    {
        foreach (var warning in runtimeWarnings)
        {
            warnings.Add(new ReportWarning
            {
                Severity = WarningSeverity.Warning,
                Category = WarningCategory.ProtectionIneffective,
                Message = warning
            });
        }
    }

    private static string FormatSkipReason(SkipReason reason) => reason switch
    {
        SkipReason.StringTooShort => "string too short",
        SkipReason.ConstantBelowThreshold => "constant below threshold",
        SkipReason.CommonFloatValue => "common float/double value",
        SkipReason.ExcludedByRule => "excluded by rule",
        SkipReason.PreservedPublicApi => "preserved public API",
        SkipReason.CompilerGenerated => "compiler-generated",
        SkipReason.UnsupportedConstruct => "unsupported construct",
        SkipReason.ResourceExcluded => "resource excluded by pattern",
        SkipReason.GenericMethod => "generic method (IL encryption)",
        _ => reason.ToString()
    };

    private FileInfoSection BuildFileInfoFromResult(ObfuscationResult result)
    {
        var inputPath = result.InputPath ?? string.Empty;
        var outputPath = result.OutputPath ?? string.Empty;

        var inputSize = TryGetFileSize(inputPath);
        var outputSize = TryGetFileSize(outputPath);

        return new FileInfoSection
        {
            InputPath = inputPath,
            OutputPath = outputPath,
            InputSizeBytes = inputSize,
            OutputSizeBytes = outputSize
        };
    }

    private static SymbolMapSummary BuildSymbolSummaryFromResult(ObfuscationResult result, ObfySettings settings)
    {
        var typeCount = 0;
        var methodCount = 0;
        var fieldCount = 0;
        var propertyCount = 0;
        var parameterCount = 0;

        foreach (var key in result.SymbolMap.Keys)
        {
            if (key.StartsWith("Type:"))
                typeCount++;
            else if (key.StartsWith("Method:"))
                methodCount++;
            else if (key.StartsWith("Field:"))
                fieldCount++;
            else if (key.StartsWith("Property:"))
                propertyCount++;
            else if (key.StartsWith("Parameter:"))
                parameterCount++;
        }

        return new SymbolMapSummary
        {
            TotalSymbolsRenamed = result.SymbolMap.Count,
            TypesRenamed = typeCount,
            MethodsRenamed = methodCount,
            FieldsRenamed = fieldCount,
            PropertiesRenamed = propertyCount,
            ParametersRenamed = parameterCount,
            NamingModeUsed = settings.SymbolRenaming.Mode
        };
    }

    private static List<ReportWarning> BuildWarningsFromResult(
        ObfuscationResult result,
        ObfySettings settings)
    {
        var warnings = new List<ReportWarning>();

        // Check for skipped items
        var skippedItems = result.SkippedItems;
        if (skippedItems.Count > 0)
        {
            var groupedSkips = skippedItems
                .GroupBy(s => s.Reason)
                .OrderByDescending(g => g.Count());

            foreach (var group in groupedSkips.Take(5))
            {
                var examples = string.Join(", ", group.Take(3).Select(i => i.ItemName));
                if (group.Count() > 3)
                {
                    examples += $", ... (+{group.Count() - 3} more)";
                }

                warnings.Add(new ReportWarning
                {
                    Severity = WarningSeverity.Info,
                    Category = WarningCategory.SkippedItem,
                    Message = $"{group.Count()} items skipped: {FormatSkipReason(group.Key)}",
                    RelatedItem = group.Key.ToString(),
                    Details = examples
                });
            }
        }

        AddRuntimeWarnings(warnings, result.Warnings);

        // Check for unused settings
        if (settings.StringEncryption.Enabled && result.Statistics.StringsEncrypted == 0)
        {
            warnings.Add(new ReportWarning
            {
                Severity = WarningSeverity.Warning,
                Category = WarningCategory.UnusedSetting,
                Message = "String encryption was enabled but no strings were encrypted",
                RelatedItem = "StringEncryption",
                Details = "The assembly may not contain string literals, or they may all be below the minimum length threshold."
            });
        }

        if (settings.ConstantEncryption.Enabled && result.Statistics.ConstantsEncrypted == 0)
        {
            warnings.Add(new ReportWarning
            {
                Severity = WarningSeverity.Warning,
                Category = WarningCategory.UnusedSetting,
                Message = "Constant encryption was enabled but no constants were encrypted",
                RelatedItem = "ConstantEncryption",
                Details = "Constants may be below the threshold or the assembly may not contain numeric literals."
            });
        }

        if (settings.ResourceEncryption.Enabled && result.Statistics.ResourcesEncrypted == 0)
        {
            warnings.Add(new ReportWarning
            {
                Severity = WarningSeverity.Warning,
                Category = WarningCategory.UnusedSetting,
                Message = "Resource encryption was enabled but no resources were encrypted",
                RelatedItem = "ResourceEncryption",
                Details = "The assembly may not contain embedded resources, or they may all be excluded by patterns."
            });
        }

        if (settings.ControlFlow.Enabled && result.Statistics.MethodsControlFlowObfuscated == 0)
        {
            warnings.Add(new ReportWarning
            {
                Severity = WarningSeverity.Warning,
                Category = WarningCategory.UnusedSetting,
                Message = "Control flow obfuscation was enabled but no methods were obfuscated",
                RelatedItem = "ControlFlow",
                Details = "Methods may be too simple, contain exception handlers, or be excluded."
            });
        }

        if (settings.SymbolRenaming.Enabled && result.Statistics.TypesRenamed == 0 &&
            result.Statistics.MethodsRenamed == 0 && result.Statistics.FieldsRenamed == 0)
        {
            warnings.Add(new ReportWarning
            {
                Severity = WarningSeverity.Warning,
                Category = WarningCategory.UnusedSetting,
                Message = "Symbol renaming was enabled but no symbols were renamed",
                RelatedItem = "SymbolRenaming",
                Details = "All symbols may be preserved due to exclusion rules or public API preservation."
            });
        }

        // Check for public API changes
        if (settings.SymbolRenaming.Enabled && !settings.SymbolRenaming.PreservePublicApi &&
            result.Statistics.TypesRenamed > 0)
        {
            warnings.Add(new ReportWarning
            {
                Severity = WarningSeverity.Important,
                Category = WarningCategory.PublicApiChange,
                Message = "Public API names may have been changed",
                RelatedItem = "PreservePublicApi",
                Details = "If this assembly is consumed by other projects, consider enabling 'Preserve Public API' to maintain compatibility."
            });
        }

        return warnings;
    }
}
