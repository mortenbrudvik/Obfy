using Mediator;
using Obfy.App.Models;
using Obfy.App.Sessions;

namespace Obfy.App.Features.Files;

/// <summary>Asks for the current input snapshot: the file list and the output row (<c>files.get</c>).</summary>
public record GetFilesRequest : IRequest<InputSnapshot>;

public class GetFilesRequestHandler(InputSession session) : IRequestHandler<GetFilesRequest, InputSnapshot>
{
    public ValueTask<InputSnapshot> Handle(GetFilesRequest request, CancellationToken cancellationToken) =>
        new(session.GetSnapshot());
}
