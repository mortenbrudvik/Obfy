using Mediator;
using Obfy.App.Models;
using Obfy.App.Sessions;

namespace Obfy.App.Features.Log;

/// <summary>Asks for the whole run log (<c>log.get</c>).</summary>
public record GetLogRequest : IRequest<LogSnapshot>;

public class GetLogRequestHandler(LogBuffer log) : IRequestHandler<GetLogRequest, LogSnapshot>
{
    public ValueTask<LogSnapshot> Handle(GetLogRequest request, CancellationToken cancellationToken) =>
        new(log.GetSnapshot());
}
