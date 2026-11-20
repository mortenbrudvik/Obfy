using Mediator;
using Obfy.App.Models;
using Obfy.App.Sessions;

namespace Obfy.App.Features.Files;

/// <summary>Removes a row by id (<c>files.remove</c>). A row already gone is not an error. Answers with the snapshot.</summary>
public record RemoveFileRequest(string Id) : IRequest<InputSnapshot>;

public class RemoveFileRequestHandler(InputSession session) : IRequestHandler<RemoveFileRequest, InputSnapshot>
{
    public ValueTask<InputSnapshot> Handle(RemoveFileRequest request, CancellationToken cancellationToken)
    {
        session.Remove(request.Id);
        return new ValueTask<InputSnapshot>(session.GetSnapshot());
    }
}
