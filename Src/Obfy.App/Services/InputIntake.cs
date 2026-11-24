using Microsoft.Extensions.Logging;
using Obfy.App.Features.Settings;
using Obfy.App.Models;
using Obfy.App.Sessions;

namespace Obfy.App.Services;

/// <summary>
/// Where paths that arrive from outside (a native drop, the file picker, the command line) go. It is the one place that
/// knows two sessions are involved: the files go to <see cref="InputSession"/>, and a configuration among them is loaded
/// into <see cref="ProtectionSession"/>. A singleton; the sessions do not know each other.
/// <para>
/// Nothing here uses <c>ConfigureAwait(false)</c>: after the configuration has been read, the load (which publishes
/// <c>settings.changed</c>) continues on the caller's context. At startup the caller is the UI thread, which is where the
/// event publisher should build the web view if this is its first publish.
/// </para>
/// </summary>
public sealed class InputIntake(InputSession input, ProtectionSession protection, RunCoordinator run, ILogger<InputIntake> logger)
{
    /// <summary>
    /// Adds the files among <paramref name="paths"/> to the list and loads the first <c>.json</c> as the configuration.
    /// Further configurations are named in a notice and not loaded. A configuration that cannot be read is a notice, not an
    /// exception.
    /// <para>
    /// While a run goes nothing is added and no configuration is loaded: a native drop has no page caller to answer, so the
    /// refusal is a notice the page shows.
    /// </para>
    /// </summary>
    public Task AddPathsAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken) =>
        AddPathsCoreAsync(paths, throwIfRunning: false, cancellationToken);

    internal Task AddPickedPathsAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken) =>
        AddPathsCoreAsync(paths, throwIfRunning: true, cancellationToken);

    private async Task AddPathsCoreAsync(IReadOnlyList<string> paths, bool throwIfRunning, CancellationToken cancellationToken)
    {
        if (throwIfRunning)
        {
            run.EnsureNotRunning();
        }
        else if (run.IsRunning)
        {
            logger.LogDebug("Refused {Count} path(s): a run is in progress", paths.Count);
            input.Notify(NoticeLevel.Warning, "Run in progress", RunCoordinator.InputBlockedMessage);
            return;
        }

        logger.LogDebug("Adding {Count} path(s)", paths.Count);
        AddPathsResult result;
        try
        {
            result = run.WhileIdle(() => input.AddPaths(paths));
        }
        catch (ObfyApiException ex) when (!throwIfRunning && ex.Code == "run.blocked")
        {
            logger.LogDebug("Refused {Count} path(s): a run is in progress", paths.Count);
            input.Notify(NoticeLevel.Warning, "Run in progress", RunCoordinator.InputBlockedMessage);
            return;
        }

        await LoadConfigAsync(result.ConfigPath, throwIfRunning, cancellationToken);
    }

    /// <summary>
    /// Applies the process-start arguments. Call on the UI thread before the shell is built: the file list changes quietly,
    /// but loading a configuration publishes <c>settings.changed</c>, and the first publish builds the web view on the
    /// thread it runs on.
    /// </summary>
    public async Task ApplyStartupAsync(IReadOnlyList<string> args, CancellationToken cancellationToken = default)
    {
        var result = input.ApplyStartup(args);
        await LoadConfigAsync(result.ConfigPath, throwIfRunning: false, cancellationToken);
    }

    private async Task LoadConfigAsync(string? path, bool throwIfRunning, CancellationToken cancellationToken)
    {
        if (path is null)
            return;

        ConfigLoadResult loaded;
        try
        {
            loaded = await ConfigFile.LoadWithUnknownKeysAsync(path, cancellationToken);
        }
        catch (ObfyApiException ex)
        {
            logger.LogWarning("Could not load a configuration ({Code})", ex.Code);
            input.ReplaceNoticeWithPrefix(
                ConfigFile.ConfigLoadErrorNoticePrefix,
                NoticeLevel.Error,
                ConfigFile.ConfigLoadErrorNoticePrefix,
                ex.Message);
            return;
        }

        try
        {
            run.WhileIdle(() =>
            {
                if (protection.ApplyOrStageConfigDrop(loaded.Settings, path, loaded.UnknownKeys))
                {
                    OpenConfigRequestHandler.ClearConfigLoadErrorNotice(input);
                    OpenConfigRequestHandler.ApplyUnknownKeysNotice(input, loaded.UnknownKeys);
                }
            });
        }
        catch (ObfyApiException ex) when (!throwIfRunning && ex.Code == "run.blocked")
        {
            logger.LogDebug("Refused loading dropped configuration: a run is in progress");
            input.Notify(NoticeLevel.Warning, "Run in progress", RunCoordinator.InputBlockedMessage);
        }
    }
}
