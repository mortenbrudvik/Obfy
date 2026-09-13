namespace Obfy.Core.Models.Solution;

/// <summary>
/// Per-project settings inferred from project type / SDK.
/// </summary>
public sealed class ProjectSettingsHints
{
    /// <summary>
    /// Preserve XAML-related public names and attributes.
    /// </summary>
    public bool PreserveXaml { get; init; }

    /// <summary>
    /// Preserve public API surface.
    /// </summary>
    public bool PreservePublicApi { get; init; }

    /// <summary>
    /// Runtime profile for this project.
    /// </summary>
    public RuntimeProfile RuntimeProfile { get; init; } = RuntimeProfile.Default;

    /// <summary>
    /// Apply Unity-oriented exclude rules.
    /// </summary>
    public bool AddUnityExcludes { get; init; }

    /// <summary>
    /// Apply ASP.NET MVC-oriented exclude rules.
    /// </summary>
    public bool AddAspNetMvcExcludes { get; init; }
}
