using Mediator;

namespace Obfy.App.Features.App;

/// <summary>The page has loaded and drawn its first frame (<c>app.ready</c>): the shell may show the window.</summary>
public record ReadyRequest : IRequest<Unit>;

/// <summary>Tells <see cref="AppReadyCoordinator"/> the page is ready.</summary>
public class ReadyRequestHandler(AppReadyCoordinator coordinator) : IRequestHandler<ReadyRequest, Unit>
{
    public ValueTask<Unit> Handle(ReadyRequest request, CancellationToken cancellationToken)
    {
        coordinator.SignalReady();
        return Unit.ValueTask;
    }
}
