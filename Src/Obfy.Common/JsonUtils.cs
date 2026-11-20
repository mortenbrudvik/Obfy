using System.Text.Json;
using System.Text.Json.Serialization;

namespace Obfy.Common;

/// <summary>
/// The single JSON entry point for IPC messages and config files: camelCase names,
/// case-insensitive reads, enums as strings. Guids serialize as strings and dates as ISO-8601 by default.
/// </summary>
public static class JsonUtils
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    public static string Serialize(object value) => JsonSerializer.Serialize(value, value.GetType(), Options);

    public static object? Deserialize(string json, Type type) => JsonSerializer.Deserialize(json, type, Options);

    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
