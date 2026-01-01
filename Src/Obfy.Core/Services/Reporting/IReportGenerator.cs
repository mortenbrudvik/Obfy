using Obfy.Core.Models;

namespace Obfy.Core.Services.Reporting;

/// <summary>
/// Interface for generating obfuscation reports in a specific format.
/// </summary>
public interface IReportGenerator
{
    /// <summary>
    /// Gets the supported file extension for this generator.
    /// </summary>
    string FileExtension { get; }

    /// <summary>
    /// Generates a report and writes it to the specified path.
    /// </summary>
    /// <param name="report">The report data to generate.</param>
    /// <param name="outputPath">The path to write the report to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task GenerateAsync(ObfuscationReport report, string outputPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a report and returns it as a string.
    /// </summary>
    /// <param name="report">The report data to generate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The generated report content.</returns>
    Task<string> GenerateToStringAsync(ObfuscationReport report, CancellationToken cancellationToken = default);
}
