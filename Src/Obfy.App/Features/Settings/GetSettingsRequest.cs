using Mediator;
using Obfy.App.Models;
using Obfy.App.Sessions;

namespace Obfy.App.Features.Settings;

/// <summary>Asks for the current settings snapshot (<c>settings.get</c>).</summary>
public record GetSettingsRequest : IRequest<SettingsSnapshot>;

public class GetSettingsRequestHandler(ProtectionSession session) : IRequestHandler<GetSettingsRequest, SettingsSnapshot>
{
    public ValueTask<SettingsSnapshot> Handle(GetSettingsRequest request, CancellationToken cancellationToken) =>
        new(session.GetSnapshot());
}
