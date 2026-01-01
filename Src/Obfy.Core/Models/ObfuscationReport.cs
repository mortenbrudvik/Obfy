namespace Obfy.Core.Models;

/// <summary>
/// Complete obfuscation report containing all metrics and analysis.
/// </summary>
public class ObfuscationReport
{
    /// <summary>
    /// Report metadata (version, timestamp, etc.)
    /// </summary>
    public ReportMetadata Metadata { get; init; } = new();

    /// <summary>
    /// Input and output file information.
    /// </summary>
    public FileInfoSection FileInfo { get; init; } = new();

    /// <summary>
    /// Obfuscation statistics.
    /// </summary>
    public ObfuscationStatistics Statistics { get; init; } = new();

    /// <summary>
    /// Symbol map summary by category.
    /// </summary>
    public SymbolMapSummary SymbolSummary { get; init; } = new();

    /// <summary>
    /// Settings used for obfuscation.
    /// </summary>
    public SettingsSummary SettingsUsed { get; init; } = new();

    /// <summary>
    /// Warnings generated during obfuscation.
    /// </summary>
    public List<ReportWarning> Warnings { get; init; } = new();

    /// <summary>
    /// Processing time breakdown by obfuscator.
    /// </summary>
    public List<ProcessingTimeEntry> ProcessingTimes { get; init; } = new();
}

/// <summary>
/// Report metadata including version and timing.
/// </summary>
public class ReportMetadata
{
    /// <summary>
    /// Obfy version used for obfuscation.
    /// </summary>
    public string ObfyVersion { get; init; } = "1.0.0";

    /// <summary>
    /// Timestamp when report was generated.
    /// </summary>
    public DateTime GeneratedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Obfuscation level used.
    /// </summary>
    public ObfuscationLevel Level { get; init; }

    /// <summary>
    /// Total elapsed time for obfuscation.
    /// </summary>
    public TimeSpan TotalElapsedTime { get; init; }
}

/// <summary>
/// Input and output file information with size comparison.
/// </summary>
public class FileInfoSection
{
    /// <summary>
    /// Path to the input file.
    /// </summary>
    public string InputPath { get; init; } = string.Empty;

    /// <summary>
    /// Path to the output file.
    /// </summary>
    public string OutputPath { get; init; } = string.Empty;

    /// <summary>
    /// Input file size in bytes.
    /// </summary>
    public long InputSizeBytes { get; init; }

    /// <summary>
    /// Output file size in bytes.
    /// </summary>
    public long OutputSizeBytes { get; init; }

    /// <summary>
    /// Size difference (output - input).
    /// </summary>
    public long SizeDifferenceBytes => OutputSizeBytes - InputSizeBytes;

    /// <summary>
    /// Size change as a percentage.
    /// </summary>
    public double SizeChangePercent => InputSizeBytes > 0
        ? Math.Round((double)SizeDifferenceBytes / InputSizeBytes * 100, 2)
        : 0;
}

/// <summary>
/// Summary of symbol renaming by category.
/// </summary>
public class SymbolMapSummary
{
    /// <summary>
    /// Total number of symbols renamed.
    /// </summary>
    public int TotalSymbolsRenamed { get; init; }

    /// <summary>
    /// Number of types renamed.
    /// </summary>
    public int TypesRenamed { get; init; }

    /// <summary>
    /// Number of methods renamed.
    /// </summary>
    public int MethodsRenamed { get; init; }

    /// <summary>
    /// Number of fields renamed.
    /// </summary>
    public int FieldsRenamed { get; init; }

    /// <summary>
    /// Number of properties renamed.
    /// </summary>
    public int PropertiesRenamed { get; init; }

    /// <summary>
    /// Number of parameters renamed.
    /// </summary>
    public int ParametersRenamed { get; init; }

    /// <summary>
    /// Naming mode used for renaming.
    /// </summary>
    public NamingMode NamingModeUsed { get; init; }
}

/// <summary>
/// Summary of settings used during obfuscation.
/// </summary>
public class SettingsSummary
{
    /// <summary>
    /// Whether string encryption was enabled.
    /// </summary>
    public bool StringEncryptionEnabled { get; init; }

    /// <summary>
    /// Whether constant encryption was enabled.
    /// </summary>
    public bool ConstantEncryptionEnabled { get; init; }

    /// <summary>
    /// Whether resource encryption was enabled.
    /// </summary>
    public bool ResourceEncryptionEnabled { get; init; }

    /// <summary>
    /// Whether control flow obfuscation was enabled.
    /// </summary>
    public bool ControlFlowEnabled { get; init; }

    /// <summary>
    /// Whether symbol renaming was enabled.
    /// </summary>
    public bool SymbolRenamingEnabled { get; init; }

    /// <summary>
    /// Whether anti-debug protection was enabled.
    /// </summary>
    public bool AntiDebugEnabled { get; init; }

    /// <summary>
    /// Whether metadata removal was enabled.
    /// </summary>
    public bool MetadataRemovalEnabled { get; init; }

    /// <summary>
    /// Whether public API was preserved.
    /// </summary>
    public bool PreservePublicApi { get; init; }
}

/// <summary>
/// Processing time entry for a single obfuscator.
/// </summary>
public class ProcessingTimeEntry
{
    /// <summary>
    /// Name of the obfuscator.
    /// </summary>
    public string ObfuscatorName { get; init; } = string.Empty;

    /// <summary>
    /// Duration of the obfuscation step.
    /// </summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// Number of transformations applied.
    /// </summary>
    public int TransformationsApplied { get; init; }
}
