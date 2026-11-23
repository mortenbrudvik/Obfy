namespace Obfy.Console;

/// <inheritdoc cref="Obfy.Core.Utilities.ConfigKeyValidator"/>
internal static class ConfigKeyValidator
{
    /// <inheritdoc cref="Obfy.Core.Utilities.ConfigKeyValidator.FindUnknownKeys"/>
    public static IReadOnlyList<string> FindUnknownKeys(string json) =>
        Obfy.Core.Utilities.ConfigKeyValidator.FindUnknownKeys(json);
}
