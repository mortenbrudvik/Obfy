using Mediator;
using Obfy.App.Models;
using Obfy.App.Sessions;

namespace Obfy.App.Features.Settings;

/// <summary>
/// Applies a dropped configuration that was staged while the settings had unsaved changes
/// (<c>settings.confirmConfigDrop</c>). Refused with <c>run.blocked</c> while a run goes. Answers with the snapshot.
/// </summary>
public record ConfirmConfigDropRequest : IRequest<SettingsSnapshot>;

public class ConfirmConfigDropRequestHandler(
    ProtectionSession session, RunCoordinator run, InputSession? input = null)
    : IRequestHandler<ConfirmConfigDropRequest, SettingsSnapshot>
{
    public ValueTask<SettingsSnapshot> Handle(ConfirmConfigDropRequest request, CancellationToken cancellationToken)
    {
        run.WhileIdle(() =>
        {
            var unknownKeys = session.ConfirmPendingConfigDrop();
            if (unknownKeys is not null)
            {
                OpenConfigRequestHandler.ClearConfigLoadErrorNotice(input);
                OpenConfigRequestHandler.ApplyUnknownKeysNotice(input, unknownKeys);
            }
        });

        return new ValueTask<SettingsSnapshot>(session.GetSnapshot());
    }
}
