using Mediator;
using Obfy.App.Models;
using Obfy.App.Sessions;
using Obfy.Core.Models;

namespace Obfy.App.Features.Settings;

/// <summary>Selects a level: Minimal, Standard, Aggressive or Custom (<c>settings.applyLevel</c>). Refused with <c>run.blocked</c> while a run goes. Answers with the new snapshot.</summary>
public record ApplyLevelRequest(string Level) : IRequest<SettingsSnapshot>;

public class ApplyLevelRequestHandler(ProtectionSession session, RunCoordinator run) : IRequestHandler<ApplyLevelRequest, SettingsSnapshot>
{
    public ValueTask<SettingsSnapshot> Handle(ApplyLevelRequest request, CancellationToken cancellationToken)
    {
        run.EnsureNotRunning();
        var name = Enum.GetNames<ObfuscationLevel>()
            .FirstOrDefault(n => n.Equals(request.Level?.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new ObfyApiException("settings.invalidValue", $"Unknown level: {request.Level}");

        session.ApplyLevel(Enum.Parse<ObfuscationLevel>(name));
        return new ValueTask<SettingsSnapshot>(session.GetSnapshot());
    }
}
