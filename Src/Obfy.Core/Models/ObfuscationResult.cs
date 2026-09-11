namespace Obfy.Core.Models;

/// <summary>
/// Represents the result of an obfuscation operation.
/// </summary>
public class ObfuscationResult
{
    /// <summary>
    /// Gets whether the obfuscation was successful.
    /// </summary>
    public bool Success { get; private init; }

    /// <summary>
    /// Gets the error message if the obfuscation failed.
    /// </summary>
    public string? ErrorMessage { get; private init; }

    /// <summary>
    /// Gets the exception that caused the failure, if any.
    /// </summary>
    public Exception? Exception { get; private init; }

    /// <summary>
    /// Gets the statistics from the obfuscation process.
    /// </summary>
    public ObfuscationStatistics Statistics { get; private init; } = new();

    /// <summary>
    /// Gets the input file path.
    /// </summary>
    public string? InputPath { get; private init; }

    /// <summary>
    /// Gets the output file path.
    /// </summary>
    public string? OutputPath { get; private init; }

    /// <summary>
    /// Gets the elapsed time for the obfuscation.
    /// </summary>
    public TimeSpan ElapsedTime { get; private init; }

    /// <summary>
    /// Gets the processing times for each obfuscator (for reporting).
    /// </summary>
    public List<ProcessingTimeEntry> ProcessingTimes { get; private init; } = new();

    /// <summary>
    /// Gets the items skipped during obfuscation (for reporting).
    /// </summary>
    public List<SkippedItem> SkippedItems { get; private init; } = new();

    /// <summary>
    /// Gets the symbol map of renamed symbols (for reporting).
    /// </summary>
    public Dictionary<string, string> SymbolMap { get; private init; } = new();

    /// <summary>
    /// Gets non-fatal warnings raised during obfuscation (e.g. a protection that could not take
    /// effect). Surfaced to the user so enabled protections never silently do nothing.
    /// </summary>
    public List<string> Warnings { get; private init; } = new();

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    public static ObfuscationResult Successful(
        ObfuscationStatistics statistics,
        string? inputPath = null,
        string? outputPath = null,
        TimeSpan? elapsedTime = null,
        List<ProcessingTimeEntry>? processingTimes = null,
        List<SkippedItem>? skippedItems = null,
        Dictionary<string, string>? symbolMap = null,
        List<string>? warnings = null)
    {
        return new ObfuscationResult
        {
            Success = true,
            Statistics = statistics,
            InputPath = inputPath,
            OutputPath = outputPath,
            ElapsedTime = elapsedTime ?? TimeSpan.Zero,
            ProcessingTimes = processingTimes ?? new(),
            SkippedItems = skippedItems ?? new(),
            SymbolMap = symbolMap ?? new(),
            Warnings = warnings ?? new()
        };
    }

    /// <summary>
    /// Creates a failed result.
    /// </summary>
    public static ObfuscationResult Failed(string errorMessage, Exception? exception = null)
    {
        return new ObfuscationResult
        {
            Success = false,
            ErrorMessage = errorMessage,
            Exception = exception
        };
    }
}

/// <summary>
/// Statistics collected during the obfuscation process.
/// </summary>
public class ObfuscationStatistics
{
    /// <summary>
    /// Number of strings encrypted.
    /// </summary>
    public int StringsEncrypted { get; set; }

    /// <summary>
    /// Number of types renamed.
    /// </summary>
    public int TypesRenamed { get; set; }

    /// <summary>
    /// Number of methods renamed.
    /// </summary>
    public int MethodsRenamed { get; set; }

    /// <summary>
    /// Number of fields renamed.
    /// </summary>
    public int FieldsRenamed { get; set; }

    /// <summary>
    /// Number of properties renamed.
    /// </summary>
    public int PropertiesRenamed { get; set; }

    /// <summary>
    /// Number of parameters renamed.
    /// </summary>
    public int ParametersRenamed { get; set; }

    /// <summary>
    /// Number of methods with obfuscated control flow.
    /// </summary>
    public int MethodsControlFlowObfuscated { get; set; }

    /// <summary>
    /// Number of protection features applied.
    /// </summary>
    public int ProtectionsApplied { get; set; }

    /// <summary>
    /// Number of metadata items removed.
    /// </summary>
    public int MetadataItemsRemoved { get; set; }

    /// <summary>
    /// Number of resources encrypted.
    /// </summary>
    public int ResourcesEncrypted { get; set; }

    /// <summary>
    /// Number of numeric constants encrypted.
    /// </summary>
    public int ConstantsEncrypted { get; set; }

    /// <summary>
    /// Total number of transformations applied.
    /// </summary>
    public int TotalTransformations =>
        StringsEncrypted +
        ConstantsEncrypted +
        TypesRenamed +
        MethodsRenamed +
        FieldsRenamed +
        PropertiesRenamed +
        ParametersRenamed +
        MethodsControlFlowObfuscated +
        ProtectionsApplied +
        MetadataItemsRemoved +
        ResourcesEncrypted;

    /// <summary>
    /// Merges another statistics instance into this one.
    /// </summary>
    public void Merge(ObfuscationStatistics other)
    {
        StringsEncrypted += other.StringsEncrypted;
        ConstantsEncrypted += other.ConstantsEncrypted;
        TypesRenamed += other.TypesRenamed;
        MethodsRenamed += other.MethodsRenamed;
        FieldsRenamed += other.FieldsRenamed;
        PropertiesRenamed += other.PropertiesRenamed;
        ParametersRenamed += other.ParametersRenamed;
        MethodsControlFlowObfuscated += other.MethodsControlFlowObfuscated;
        ProtectionsApplied += other.ProtectionsApplied;
        MetadataItemsRemoved += other.MetadataItemsRemoved;
        ResourcesEncrypted += other.ResourcesEncrypted;
    }
}
