using Mediator;
using Obfy.App.Models;
using Obfy.App.Services;
using Obfy.App.Sessions;

namespace Obfy.App.Features.Settings;

/// <summary>
/// Asks for a configuration file through the native picker and loads it (<c>settings.openConfig</c>). The page never
/// names a path. Cancelling leaves the settings as they were. Answers with the snapshot.
/// </summary>
public record OpenConfigRequest : IRequest<SettingsSnapshot>;

/// <summary>
/// Loads a configuration from a path the host already holds: a dropped file or a startup argument. Not registered for the
/// page.
/// </summary>
public record LoadConfigRequest(string Path) : IRequest<SettingsSnapshot>;

public class OpenConfigRequestHandler(ProtectionSession session, IFileDialogs dialogs) : IRequestHandler<OpenConfigRequest, SettingsSnapshot>
{
    public async ValueTask<SettingsSnapshot> Handle(OpenConfigRequest request, CancellationToken cancellationToken)
    {
        var picked = await dialogs.PickFilesAsync("Load Configuration", ConfigFile.DialogFilter, multi: false).ConfigureAwait(false);
        if (picked.Length > 0 && !string.IsNullOrEmpty(picked[0]))
            session.FromObfySettings(await ConfigFile.LoadAsync(picked[0], cancellationToken).ConfigureAwait(false), picked[0]);

        return session.GetSnapshot();
    }
}

public class LoadConfigRequestHandler(ProtectionSession session) : IRequestHandler<LoadConfigRequest, SettingsSnapshot>
{
    public async ValueTask<SettingsSnapshot> Handle(LoadConfigRequest request, CancellationToken cancellationToken)
    {
        session.FromObfySettings(await ConfigFile.LoadAsync(request.Path, cancellationToken).ConfigureAwait(false), request.Path);
        return session.GetSnapshot();
    }
}
