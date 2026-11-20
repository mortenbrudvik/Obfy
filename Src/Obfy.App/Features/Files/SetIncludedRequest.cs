using Mediator;
using Obfy.App.Models;
using Obfy.App.Sessions;

namespace Obfy.App.Features.Files;

/// <summary>Includes or excludes a row by id (<c>files.setIncluded</c>). Answers with the snapshot.</summary>
public record SetIncludedRequest(string Id, bool Included) : IRequest<InputSnapshot>;

public class SetIncludedRequestHandler(InputSession session) : IRequestHandler<SetIncludedRequest, InputSnapshot>
{
    public ValueTask<InputSnapshot> Handle(SetIncludedRequest request, CancellationToken cancellationToken)
    {
        session.SetIncluded(request.Id, request.Included);
        return new ValueTask<InputSnapshot>(session.GetSnapshot());
    }
}
