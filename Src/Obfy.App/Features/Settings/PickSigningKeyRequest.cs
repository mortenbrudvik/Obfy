using Mediator;
using Obfy.App.Models;
using Obfy.App.Services;
using Obfy.App.Sessions;

namespace Obfy.App.Features.Settings;

/// <summary>
/// Asks for a strong-name key file through the native picker and sets <c>signing.keyFile</c> to it
/// (<c>settings.pickSigningKey</c>). Cancelling changes nothing. Refused with <c>run.blocked</c> while a run goes. Answers with the snapshot.
/// </summary>
public record PickSigningKeyRequest : IRequest<SettingsSnapshot>;

public class PickSigningKeyRequestHandler(ProtectionSession session, IFileDialogs dialogs, RunCoordinator run) : IRequestHandler<PickSigningKeyRequest, SettingsSnapshot>
{
    public async ValueTask<SettingsSnapshot> Handle(PickSigningKeyRequest request, CancellationToken cancellationToken)
    {
        run.EnsureNotRunning();
        var path = await dialogs.PickKeyAsync().ConfigureAwait(false);
        if (!string.IsNullOrEmpty(path))
            run.WhileIdle(() => session.UpdateSetting("signing.keyFile", path));

        return session.GetSnapshot();
    }
}
