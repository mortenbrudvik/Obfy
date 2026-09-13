namespace Obfy.Core.Utilities;

/// <summary>
/// Platform-specific exclusion patterns shared by solution hints and the configuration wizard.
/// </summary>
public static class PlatformExclusions
{
    /// <summary>
    /// Unity engine namespaces to exclude from renaming.
    /// </summary>
    public static readonly IReadOnlyList<string> UnityNamespaces =
    [
        "UnityEngine",
        "UnityEngine.*",
        "Unity",
        "Unity.*"
    ];

    /// <summary>
    /// ASP.NET Core MVC attributes to exclude (matches <c>ConfigurationWizard.ApplyUseCaseDefaults</c>).
    /// </summary>
    public static readonly IReadOnlyList<string> AspNetMvcAttributes =
    [
        "Microsoft.AspNetCore.Mvc.RouteAttribute",
        "Microsoft.AspNetCore.Mvc.ApiControllerAttribute",
        "Microsoft.AspNetCore.Mvc.HttpGetAttribute",
        "Microsoft.AspNetCore.Mvc.HttpPostAttribute",
        "Microsoft.AspNetCore.Mvc.HttpPutAttribute",
        "Microsoft.AspNetCore.Mvc.HttpDeleteAttribute"
    ];
}
