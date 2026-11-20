using Mediator;
using Obfy.App.Services;

namespace Obfy.App.Features.Files;

/// <summary>
/// Files dropped onto the window. The shell window raises it from its native drop (the page takes no drop), so it has no
/// page-facing name in <c>ApiRegistrations</c>. Handled like <c>files.pick</c>: supported files join the list, solutions
/// and projects expand, and the first <c>.json</c> loads as the configuration. While a run goes the drop is refused with a
/// notice (see <see cref="InputIntake.AddPathsAsync"/>).
/// </summary>
public record DropPathsRequest(IReadOnlyList<string> Paths) : IRequest<Unit>;

public class DropPathsRequestHandler(InputIntake intake) : IRequestHandler<DropPathsRequest, Unit>
{
    public async ValueTask<Unit> Handle(DropPathsRequest request, CancellationToken cancellationToken)
    {
        await intake.AddPathsAsync(request.Paths, cancellationToken);
        return Unit.Value;
    }
}
