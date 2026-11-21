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
}
