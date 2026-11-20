using Mediator;
using Obfy.App.Models;
using Obfy.App.Sessions;

namespace Obfy.App.Features.Files;

/// <summary>Removes a row by id (<c>files.remove</c>). A row already gone is not an error. Refused with <c>run.blocked</c> while a run goes. Answers with the snapshot.</summary>
public record RemoveFileRequest(string Id) : IRequest<InputSnapshot>;

public class RemoveFileRequestHandler(InputSession session, RunCoordinator run) : IRequestHandler<RemoveFileRequest, InputSnapshot>
{
    public ValueTask<InputSnapshot> Handle(RemoveFileRequest request, CancellationToken cancellationToken)
    {
        run.EnsureNotRunning();
        session.Remove(request.Id);
        return new ValueTask<InputSnapshot>(session.GetSnapshot());
    }
}
