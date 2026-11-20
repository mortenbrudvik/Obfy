using Mediator;
using Microsoft.Extensions.Logging;

namespace Obfy.App.Features.Files;

/// <summary>
/// Files or folders dropped onto the window. The shell window raises it from its native drop (the page takes no drop), so
/// it has no page-facing name in <c>ApiRegistrations</c>.
/// </summary>
public record DropPathsRequest(IReadOnlyList<string> Paths) : IRequest<Unit>;

/// <summary>Spike stub: logs the dropped paths. Task 10 replaces it with the real file-list behaviour.</summary>
public class DropPathsRequestHandler(ILogger<DropPathsRequestHandler> logger) : IRequestHandler<DropPathsRequest, Unit>
{
    public ValueTask<Unit> Handle(DropPathsRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Dropped {Count} path(s): {Paths}", request.Paths.Count, string.Join(" | ", request.Paths));
        return Unit.ValueTask;
    }
}
