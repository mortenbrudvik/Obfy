using Mediator;
using Obfy.App.Models;
using Obfy.App.Sessions;

namespace Obfy.App.Features.Files;

/// <summary>Empties the file list (<c>files.clear</c>). Refused with <c>run.blocked</c> while a run goes. Answers with the snapshot.</summary>
public record ClearFilesRequest : IRequest<InputSnapshot>;

public class ClearFilesRequestHandler(InputSession session, RunCoordinator run) : IRequestHandler<ClearFilesRequest, InputSnapshot>
{
    public ValueTask<InputSnapshot> Handle(ClearFilesRequest request, CancellationToken cancellationToken)
    {
        run.WhileIdle(() => session.Clear());
        return new ValueTask<InputSnapshot>(session.GetSnapshot());
    }
}
