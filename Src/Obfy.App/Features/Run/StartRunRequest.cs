using Mediator;
using Obfy.App.Models;
using Obfy.App.Sessions;

namespace Obfy.App.Features.Run;

/// <summary>
/// Starts a run (<c>run.start</c>) and answers at once with the running snapshot; progress and the end arrive as
/// <c>run.changed</c> and <c>run.completed</c>. Throws <c>run.blocked</c> with the reason when the run cannot start.
/// </summary>
public record StartRunRequest : IRequest<RunSnapshot>;

public class StartRunRequestHandler(RunCoordinator run) : IRequestHandler<StartRunRequest, RunSnapshot>
{
    public ValueTask<RunSnapshot> Handle(StartRunRequest request, CancellationToken cancellationToken)
    {
        // The run's own task is not awaited: the request must not wait for the run, and the run never throws.
        _ = run.Start();
        return new ValueTask<RunSnapshot>(run.GetSnapshot());
    }
}
