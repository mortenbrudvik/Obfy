namespace Obfy.Core.Services;

/// <summary>Shared wording for warnings the orchestration services add to a result.</summary>
internal static class ObfuscationWarnings
{
    /// <summary>
    /// The run succeeded but changed nothing: no technique is enabled, or the enabled ones found nothing to
    /// transform in this input.
    /// </summary>
    public const string NoTransformations =
        "No transformations were applied: no enabled technique found anything to change, " +
        "so the output is effectively a copy of the input.";

    /// <summary>
    /// <c>incremental.enabled</c> is on for a run the cache does not cover: a closed set (several assemblies, a solution or a
    /// project). Said once per run, on the first module. Fixed text: it names no file.
    /// </summary>
    public const string ClosedSetCacheNote =
        "Incremental cache not used: it covers a single assembly or a merge, not a closed set " +
        "(a solution, a project, or several assemblies protected together), so every assembly was obfuscated.";

    /// <summary>
    /// <c>incremental.enabled</c> is on for C# source (a file, several files or a folder), which the cache does not cover.
    /// Said once per run. Fixed text: it names no file.
    /// </summary>
    public const string SourceCacheNote =
        "Incremental cache not used: it covers a single assembly or a merge, not C# source " +
        "(a file, several files or a folder), so every file was obfuscated.";
}
