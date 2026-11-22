using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace Settings.Core.Validation;

/// <summary>
/// Provides validation for settings objects using DataAnnotations.
/// </summary>
public static class SettingsValidator
{
    /// <summary>
    /// Validates the settings object and throws ValidationException if invalid.
    /// </summary>
    /// <typeparam name="T">The settings type.</typeparam>
    /// <param name="settings">The settings instance to validate.</param>
    /// <exception cref="ValidationException">Thrown when validation fails.</exception>
    public static void Validate<T>(T settings) where T : class
    {
#if NET6_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(settings);
#else
        if (settings == null)
            throw new ArgumentNullException(nameof(settings));
#endif

        var results = new List<ValidationResult>();
        if (!TryValidate(settings, results))
        {
            var errors = string.Join("; ", results.Select(r => r.ErrorMessage));
            throw new ValidationException($"Settings validation failed: {errors}");
        }
    }

    /// <summary>
    /// Attempts to validate the settings object.
    /// </summary>
    /// <typeparam name="T">The settings type.</typeparam>
    /// <param name="settings">The settings instance to validate.</param>
    /// <param name="results">Collection to receive validation errors.</param>
    /// <returns>True if valid, false otherwise.</returns>
    public static bool TryValidate<T>(T settings, ICollection<ValidationResult> results) where T : class
    {
#if NET6_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(results);
#else
        if (settings == null)
            throw new ArgumentNullException(nameof(settings));
        if (results == null)
            throw new ArgumentNullException(nameof(results));
#endif

        var context = new ValidationContext(settings);
        var isValid = Validator.TryValidateObject(settings, context, results, validateAllProperties: true);

        // Recursively validate nested objects
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance) { settings };
        ValidateNestedObjects(settings, string.Empty, visited, results, ref isValid);

        return isValid;
    }

    /// <summary>
    /// Attempts to validate the settings object without collecting results.
    /// </summary>
    /// <typeparam name="T">The settings type.</typeparam>
    /// <param name="settings">The settings instance to validate.</param>
    /// <returns>True if valid, false otherwise.</returns>
    public static bool IsValid<T>(T settings) where T : class
    {
        var results = new List<ValidationResult>();
        return TryValidate(settings, results);
    }

    private static void ValidateNestedObjects(
        object current,
        string prefix,
        HashSet<object> visited,
        ICollection<ValidationResult> results,
        ref bool isValid)
    {
        var properties = current.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(static p => p.CanRead
                && p.GetIndexParameters().Length == 0
                && p.PropertyType.IsClass
                && p.PropertyType != typeof(string));

        foreach (var property in properties)
        {
            var value = property.GetValue(current);
            if (value == null || !visited.Add(value))
                continue;

            var path = string.IsNullOrEmpty(prefix) ? property.Name : $"{prefix}.{property.Name}";
            var nestedContext = new ValidationContext(value);
            var nestedResults = new List<ValidationResult>();

            if (!Validator.TryValidateObject(value, nestedContext, nestedResults, validateAllProperties: true))
            {
                isValid = false;
                foreach (var result in nestedResults)
                {
                    results.Add(new ValidationResult(
                        $"{path}.{result.ErrorMessage}",
                        result.MemberNames.Select(m => $"{path}.{m}")));
                }
            }

            ValidateNestedObjects(value, path, visited, results, ref isValid);
        }
    }
}
