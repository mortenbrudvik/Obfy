using Obfy.Core.Models;
using Obfy.Core.Pipeline;

namespace Obfy.Core.Services.Reporting;

/// <summary>
/// Service for generating and managing obfuscation reports.
/// </summary>
public interface IReportService
{
    /// <summary>
    /// Builds a report from the pipeline context and result.
    /// </summary>
    /// <param name="context">The pipeline context containing obfuscation data.</param>
    /// <param name="result">The obfuscation result.</param>
    /// <param name="settings">The settings used for obfuscation.</param>
    /// <returns>A complete obfuscation report.</returns>
    ObfuscationReport BuildReport(
        PipelineContext context,
        ObfuscationResult result,
        ObfySettings settings);

    /// <summary>
    /// Builds a report from the obfuscation result (with embedded context data).
    /// </summary>
    /// <param name="result">The obfuscation result containing context data.</param>
    /// <param name="settings">The settings used for obfuscation.</param>
    /// <returns>A complete obfuscation report.</returns>
    ObfuscationReport BuildReport(
        ObfuscationResult result,
        ObfySettings settings);

    /// <summary>
    /// Generates a report file in the specified format.
    /// </summary>
    /// <param name="report">The report to generate.</param>
    /// <param name="outputPath">The path to write the report to.</param>
    /// <param name="format">The report format.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task GenerateReportAsync(
        ObfuscationReport report,
        string outputPath,
        ReportFormat format,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the appropriate file extension for the format.
    /// </summary>
    /// <param name="format">The report format.</param>
    /// <returns>The file extension including the leading dot.</returns>
    string GetFileExtension(ReportFormat format);
}

/// <summary>
/// Supported report output formats.
/// </summary>
public enum ReportFormat
{
    /// <summary>
    /// HTML format with visual styling.
    /// </summary>
    Html,

    /// <summary>
    /// JSON format for machine processing.
    /// </summary>
    Json
}
