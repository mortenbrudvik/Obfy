using System.Text.Json;
using Mediator;
using Obfy.App.Models;
using Obfy.App.Services;
using Obfy.App.Sessions;

namespace Obfy.App.Features.Results;

/// <summary>
/// Writes the symbol map as indented JSON to a file the user picks (<c>results.exportMap</c>). Cancelling the dialog
/// writes nothing and answers <c>exported: false</c>. A write failure reaches the page as an error with the reason.
/// </summary>
public record ExportMapRequest : IRequest<ExportResult>;

public class ExportMapRequestHandler(ResultsStore results, IFileDialogs dialogs) : IRequestHandler<ExportMapRequest, ExportResult>
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public async ValueTask<ExportResult> Handle(ExportMapRequest request, CancellationToken cancellationToken)
    {
        var map = results.GetSymbolMap();
        if (map.Count == 0)
            throw new ObfyApiException("results.noSymbolMap", "There is no symbol map to export yet.");

        var path = await dialogs.PickSaveAsync(
            "Export Symbol Map",
            "JSON Files (*.json)|*.json|Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
            "symbolmap.json").ConfigureAwait(false);
        if (string.IsNullOrEmpty(path))
            return new ExportResult(false, null);

        try
        {
            var json = JsonSerializer.Serialize(map, Indented);
            await File.WriteAllTextAsync(path, json, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ObfyApiException("results.exportFailed", ex.Message);
        }

        return new ExportResult(true, path);
    }
}
