using Mediator;
using Obfy.App.Models;
using Obfy.App.Services;
using Obfy.App.Sessions;

namespace Obfy.App.Features.Output;

/// <summary>Asks for the output folder through the native picker (<c>output.pickFolder</c>). Cancelling changes nothing. Answers with the snapshot.</summary>
public record PickOutputFolderRequest : IRequest<InputSnapshot>;

public class PickOutputFolderRequestHandler(InputSession session, IFileDialogs dialogs)
    : IRequestHandler<PickOutputFolderRequest, InputSnapshot>
{
    public async ValueTask<InputSnapshot> Handle(PickOutputFolderRequest request, CancellationToken cancellationToken)
    {
        var folder = await dialogs.PickFolderAsync("Select Output Directory").ConfigureAwait(false);
        if (!string.IsNullOrEmpty(folder))
            session.SetOutputFolder(folder);

        return session.GetSnapshot();
    }
}
