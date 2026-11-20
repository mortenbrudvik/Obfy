using Mediator;
using Obfy.App.Models;
using Obfy.App.Sessions;

namespace Obfy.App.Features.Log;

/// <summary>Empties the run log (<c>log.clear</c>). Answers with the snapshot.</summary>
public record ClearLogRequest : IRequest<LogSnapshot>;

public class ClearLogRequestHandler(LogBuffer log) : IRequestHandler<ClearLogRequest, LogSnapshot>
{
    public ValueTask<LogSnapshot> Handle(ClearLogRequest request, CancellationToken cancellationToken)
    {
        log.Clear();
        return new ValueTask<LogSnapshot>(log.GetSnapshot());
    }
}
