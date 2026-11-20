using Mediator;
using Obfy.App.Models;
using Obfy.App.Sessions;

namespace Obfy.App.Features.Results;

/// <summary>Asks for the results state and statistics (<c>results.summary</c>). The page tells "no run yet" from "run finished, nothing to show" by <see cref="ResultsSnapshot.State"/>.</summary>
public record GetResultsSummaryRequest : IRequest<ResultsSnapshot>;

public class GetResultsSummaryRequestHandler(ResultsStore results) : IRequestHandler<GetResultsSummaryRequest, ResultsSnapshot>
{
    public ValueTask<ResultsSnapshot> Handle(GetResultsSummaryRequest request, CancellationToken cancellationToken) =>
        new(results.GetSnapshot());
}
