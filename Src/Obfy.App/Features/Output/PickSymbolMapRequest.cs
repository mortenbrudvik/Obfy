using Mediator;
using Obfy.App.Models;
using Obfy.App.Services;
using Obfy.App.Sessions;

namespace Obfy.App.Features.Output;

/// <summary>
/// Asks for the symbol-map file through the native save dialog (<c>output.pickSymbolMap</c>). Cancelling changes nothing.
/// The path can only be set while the map is on, as with <c>output.setSymbolMap</c>. Answers with the snapshot.
/// </summary>
public record PickSymbolMapRequest : IRequest<InputSnapshot>;

public class PickSymbolMapRequestHandler(InputSession session, IFileDialogs dialogs)
    : IRequestHandler<PickSymbolMapRequest, InputSnapshot>
{
    public async ValueTask<InputSnapshot> Handle(PickSymbolMapRequest request, CancellationToken cancellationToken)
    {
        var path = await dialogs.PickSaveAsync("Save symbol map", "JSON files (*.json)|*.json|All files (*.*)|*.*", "symbolmap.json")
            .ConfigureAwait(false);
        if (!string.IsNullOrEmpty(path))
            session.SetSymbolMapPath(path);

        return session.GetSnapshot();
    }
}
