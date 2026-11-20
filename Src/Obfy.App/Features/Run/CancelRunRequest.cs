using Mediator;
using Obfy.App.Models;
using Obfy.App.Sessions;

namespace Obfy.App.Features.Run;

/// <summary>Asks the running run to stop (<c>run.cancel</c>). Nothing happens when none is running. Answers with the snapshot.</summary>
public record CancelRunRequest : IRequest<RunSnapshot>;

public class CancelRunRequestHandler(RunCoordinator run) : IRequestHandler<CancelRunRequest, RunSnapshot>
{
    public ValueTask<RunSnapshot> Handle(CancelRunRequest request, CancellationToken cancellationToken)
    {
        run.Cancel();
        return new ValueTask<RunSnapshot>(run.GetSnapshot());
    }
}
