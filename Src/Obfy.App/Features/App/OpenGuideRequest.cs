using Mediator;
using Obfy.App.Services;

namespace Obfy.App.Features.App;

/// <summary>
/// Opens the online guide in the default browser (<c>app.openGuide</c>). <paramref name="Topic"/> is an id from
/// <see cref="GuideLinks.Topics"/>; an unknown or missing one opens the guide's start. The page never supplies an address.
/// </summary>
public record OpenGuideRequest(string? Topic) : IRequest<Unit>;

public class OpenGuideRequestHandler(IShellLauncher shell) : IRequestHandler<OpenGuideRequest, Unit>
{
    public ValueTask<Unit> Handle(OpenGuideRequest request, CancellationToken cancellationToken)
    {
        try
        {
            shell.OpenUrl(GuideLinks.Resolve(request.Topic));
        }
        catch (IOException)
        {
            // The cause (and the topic) stay out of the reply; the guide's address is public.
            throw new ObfyApiException("app.openGuideFailed", $"The guide could not be opened in your browser. Visit {GuideLinks.BaseUrl} instead.");
        }

        return Unit.ValueTask;
    }
}
