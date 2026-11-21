using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Obfy.Core.Models;

namespace Obfy.Console;

/// <summary>
/// The one way the command line writes an <c>obfy.json</c> (<c>config generate</c> and the wizard): camelCase names,
/// enums as names and no nulls, so the file validates against the published schema, which rejects enum numbers
/// and <c>null</c> paths.
/// </summary>
internal static class ConfigFileFormat
{
    internal const string SchemaUrl = "https://raw.githubusercontent.com/mortenbrudvik/Obfy/main/schemas/obfy.schema.json";

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Serialize(ObfySettings settings)
    {
        var json = JsonSerializer.Serialize(settings, WriteOptions);
        var node = JsonNode.Parse(json)!.AsObject();
        node.Insert(0, "$schema", SchemaUrl);
        return node.ToJsonString(WriteOptions);
    }
}
