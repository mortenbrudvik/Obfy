using Mediator;
using Obfy.App.Models;
using Obfy.App.Sessions;

namespace Obfy.App.Features.Settings;

/// <summary>
/// Discards a dropped configuration that was awaiting confirmation (<c>settings.cancelConfigDrop</c>), leaving the
/// current unsaved settings untouched. Answers with the snapshot.
/// </summary>
public record CancelConfigDropRequest : IRequest<SettingsSnapshot>;

public class CancelConfigDropRequestHandler(ProtectionSession session)
    : IRequestHandler<CancelConfigDropRequest, SettingsSnapshot>
{
    public ValueTask<SettingsSnapshot> Handle(CancelConfigDropRequest request, CancellationToken cancellationToken)
    {
        session.CancelPendingConfigDrop();
        return new ValueTask<SettingsSnapshot>(session.GetSnapshot());
    }
}
