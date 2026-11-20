using Mediator;
using Obfy.App.Models;
using Obfy.App.Sessions;

namespace Obfy.App.Features.Run;

/// <summary>Asks whether a run can start and how it is going (<c>run.get</c>).</summary>
public record GetRunRequest : IRequest<RunSnapshot>;

public class GetRunRequestHandler(RunCoordinator run) : IRequestHandler<GetRunRequest, RunSnapshot>
{
    public ValueTask<RunSnapshot> Handle(GetRunRequest request, CancellationToken cancellationToken) =>
        new(run.GetSnapshot());
}
