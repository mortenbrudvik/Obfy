namespace Obfy.Core.Models;

/// <summary>
/// Represents the result of an obfuscation operation.
/// </summary>
public class ObfuscationResult
{
    /// <summary>
    /// Gets whether the obfuscation was successful.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Gets the error message if the obfuscation failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Gets the exception that caused the failure, if any.
    /// </summary>
    public Exception? Exception { get; init; }

    /// <summary>
    /// Gets the statistics from the obfuscation process.
    /// </summary>
    public ObfuscationStatistics Statistics { get; init; } = new();

    /// <summary>
    /// Gets the output file path.
    /// </summary>
    public string? OutputPath { get; init; }

    /// <summary>
    /// Gets the elapsed time for the obfuscation.
    /// </summary>
    public TimeSpan ElapsedTime { get; init; }

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    public static ObfuscationResult Successful(
        ObfuscationStatistics statistics,
        string? outputPath = null,
        TimeSpan? elapsedTime = null)
    {
        return new ObfuscationResult
        {
            Success = true,
            Statistics = statistics,
            OutputPath = outputPath,
            ElapsedTime = elapsedTime ?? TimeSpan.Zero
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
