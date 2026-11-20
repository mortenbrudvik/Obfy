using Mediator;
using Obfy.App.Models;
using Obfy.App.Services;
using Obfy.App.Sessions;

namespace Obfy.App.Features.Settings;

/// <summary>
/// Asks for a strong-name key file through the native picker and sets <c>signing.keyFile</c> to it
/// (<c>settings.pickSigningKey</c>). Cancelling changes nothing. Answers with the snapshot.
/// </summary>
public record PickSigningKeyRequest : IRequest<SettingsSnapshot>;

public class PickSigningKeyRequestHandler(ProtectionSession session, IFileDialogs dialogs) : IRequestHandler<PickSigningKeyRequest, SettingsSnapshot>
{
    public async ValueTask<SettingsSnapshot> Handle(PickSigningKeyRequest request, CancellationToken cancellationToken)
    {
        var path = await dialogs.PickKeyAsync().ConfigureAwait(false);
        if (!string.IsNullOrEmpty(path))
            session.UpdateSetting("signing.keyFile", path);

        return session.GetSnapshot();
    }
}
