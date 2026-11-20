using Mediator;
using Obfy.App.Models;
using Obfy.App.Services;
using Obfy.App.Sessions;

namespace Obfy.App.Features.Settings;

/// <summary>
/// Writes the settings to a configuration file (<c>settings.saveConfig</c>). Saves over the file the settings came from or
/// were last saved to; with <paramref name="SaveAs"/>, or when there is none, asks through the native picker. Cancelling
/// saves nothing. Answers with the snapshot.
/// </summary>
public record SaveConfigRequest(bool SaveAs = false) : IRequest<SettingsSnapshot>;

public class SaveConfigRequestHandler(ProtectionSession session, IFileDialogs dialogs) : IRequestHandler<SaveConfigRequest, SettingsSnapshot>
{
    public async ValueTask<SettingsSnapshot> Handle(SaveConfigRequest request, CancellationToken cancellationToken)
    {
        var path = request.SaveAs ? null : session.ConfigPath;
        path ??= await dialogs.PickSaveAsync(
            "Save Configuration", ConfigFile.DialogFilter,
            session.ConfigPath is { } current ? Path.GetFileName(current) : "obfy.json").ConfigureAwait(false);

        if (!string.IsNullOrEmpty(path))
        {
            await ConfigFile.SaveAsync(session.ToObfySettings(), path, cancellationToken).ConfigureAwait(false);
            session.MarkSaved(path);
        }

        return session.GetSnapshot();
    }
}
