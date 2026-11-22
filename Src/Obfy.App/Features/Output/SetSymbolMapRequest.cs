using Mediator;
using Obfy.App.Models;
using Obfy.App.Sessions;

namespace Obfy.App.Features.Output;

/// <summary>
/// Turns the symbol map on or off and/or sets its path (<c>output.setSymbolMap</c>). Each field left out stays as it is;
/// an empty <paramref name="Path"/> goes back to the default. The path can only be set while the map is on (the same call
/// may turn it on). Refused with <c>run.blocked</c> while a run goes. Answers with the snapshot.
/// </summary>
public record SetSymbolMapRequest(bool? Enabled = null, string? Path = null) : IRequest<InputSnapshot>;

public class SetSymbolMapRequestHandler(InputSession session, RunCoordinator run) : IRequestHandler<SetSymbolMapRequest, InputSnapshot>
{
    public ValueTask<InputSnapshot> Handle(SetSymbolMapRequest request, CancellationToken cancellationToken)
    {
        run.WhileIdle(() => session.SetSymbolMap(request.Enabled, request.Path));
        return new ValueTask<InputSnapshot>(session.GetSnapshot());
    }
}
