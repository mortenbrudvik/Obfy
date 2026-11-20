using Mediator;
using Obfy.App.Models;
using Obfy.App.Services;
using Obfy.App.Sessions;
using Obfy.Core.Services.Reporting;

namespace Obfy.App.Features.Results;

/// <summary>
/// Writes the obfuscation report to a file the user picks (<c>results.exportReport</c>): JSON for a <c>.json</c> name,
/// HTML for any other. Cancelling writes nothing and answers <c>exported: false</c>. Without a report it is an error.
/// </summary>
public record ExportReportRequest : IRequest<ExportResult>;

public class ExportReportRequestHandler(ResultsStore results, IReportService reports, IFileDialogs dialogs)
    : IRequestHandler<ExportReportRequest, ExportResult>
{
    public async ValueTask<ExportResult> Handle(ExportReportRequest request, CancellationToken cancellationToken)
    {
        var report = results.GetReport()
            ?? throw new ObfyApiException("results.noReport", "There is no report to export yet.");

        var path = await dialogs.PickSaveAsync(
            "Export Obfuscation Report",
            "HTML Report (*.html)|*.html|JSON Report (*.json)|*.json",
            "obfuscation-report.html").ConfigureAwait(false);
        if (string.IsNullOrEmpty(path))
            return new ExportResult(false, null);

        var format = Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase)
            ? ReportFormat.Json
            : ReportFormat.Html;

        try
        {
            await reports.GenerateReportAsync(report, path, format, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ObfyApiException("results.exportFailed", ex.Message);
        }

        return new ExportResult(true, path);
    }
}
