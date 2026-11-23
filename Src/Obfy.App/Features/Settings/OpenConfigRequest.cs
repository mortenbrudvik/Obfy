using Mediator;
using Obfy.App.Models;
using Obfy.App.Services;
using Obfy.App.Sessions;

namespace Obfy.App.Features.Settings;

/// <summary>
/// Asks for a configuration file through the native picker and loads it (<c>settings.openConfig</c>). The page never
/// names a path. Cancelling leaves the settings as they were. Refused with <c>run.blocked</c> while a run goes. Answers with the snapshot.
/// </summary>
public record OpenConfigRequest : IRequest<SettingsSnapshot>;

/// <summary>
/// Loads a configuration from a path the host already holds: a dropped file or a startup argument. Not registered for the
/// page.
/// </summary>
public record LoadConfigRequest(string Path) : IRequest<SettingsSnapshot>;

public class OpenConfigRequestHandler(
    ProtectionSession session, IFileDialogs dialogs, RunCoordinator run, InputSession? input = null)
    : IRequestHandler<OpenConfigRequest, SettingsSnapshot>
{
    public async ValueTask<SettingsSnapshot> Handle(OpenConfigRequest request, CancellationToken cancellationToken)
    {
        run.EnsureNotRunning();
        var picked = await dialogs.PickFilesAsync("Load Configuration", ConfigFile.DialogFilter, multi: false).ConfigureAwait(false);
        if (picked.Length > 0 && !string.IsNullOrEmpty(picked[0]))
        {
            var loaded = await ConfigFile.LoadWithUnknownKeysAsync(picked[0], cancellationToken).ConfigureAwait(false);
            ApplyLoadedConfig(session, input, run, loaded, picked[0]);
        }

        return session.GetSnapshot();
    }

    internal static void ApplyLoadedConfig(
        ProtectionSession session, InputSession? input, RunCoordinator run, ConfigLoadResult loaded, string path)
    {
        run.WhileIdle(() =>
        {
            session.FromObfySettings(loaded.Settings, path);
            input?.ReplaceNoticeWithPrefix(
                ConfigFile.UnknownKeysNoticePrefix,
                NoticeLevel.Warning,
                "Unknown configuration keys",
                loaded.UnknownKeys.Count > 0 ? ConfigFile.FormatUnknownKeysNotice(loaded.UnknownKeys) : null);
        });
    }
}

public class LoadConfigRequestHandler(
    ProtectionSession session, RunCoordinator run, InputSession? input = null)
    : IRequestHandler<LoadConfigRequest, SettingsSnapshot>
{
    public async ValueTask<SettingsSnapshot> Handle(LoadConfigRequest request, CancellationToken cancellationToken)
    {
        run.EnsureNotRunning();
        var loaded = await ConfigFile.LoadWithUnknownKeysAsync(request.Path, cancellationToken).ConfigureAwait(false);
        OpenConfigRequestHandler.ApplyLoadedConfig(session, input, run, loaded, request.Path);
        return session.GetSnapshot();
    }
}
