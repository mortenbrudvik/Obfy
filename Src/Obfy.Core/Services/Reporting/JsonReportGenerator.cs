using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Obfy.Core.Models;

namespace Obfy.Core.Services.Reporting;

/// <summary>
/// Generates JSON format obfuscation reports.
/// </summary>
public class JsonReportGenerator : IReportGenerator
{
    private readonly ILogger<JsonReportGenerator> _logger;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    /// <summary>
    /// Creates a new JSON report generator.
    /// </summary>
    public JsonReportGenerator(ILogger<JsonReportGenerator> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public string FileExtension => ".json";

    /// <inheritdoc/>
    public async Task GenerateAsync(ObfuscationReport report, string outputPath, CancellationToken cancellationToken = default)
    {
        var json = await GenerateToStringAsync(report, cancellationToken).ConfigureAwait(false);

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(outputPath, json, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("JSON report written to {OutputPath}", outputPath);
    }

    /// <inheritdoc/>
    public Task<string> GenerateToStringAsync(ObfuscationReport report, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(report, _jsonOptions);
        return Task.FromResult(json);
    }
}
