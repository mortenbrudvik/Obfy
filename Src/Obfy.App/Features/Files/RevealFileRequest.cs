using Mediator;
using Obfy.App.Models;
using Obfy.App.Sessions;

namespace Obfy.App.Features.Files;

/// <summary>
/// Shows a row's output in the file browser with the file selected (<c>files.reveal</c>). A row without output is a notice
/// in the snapshot. Answers with the snapshot.
/// </summary>
public record RevealFileRequest(string Id) : IRequest<InputSnapshot>;

public class RevealFileRequestHandler(InputSession session) : IRequestHandler<RevealFileRequest, InputSnapshot>
{
    public ValueTask<InputSnapshot> Handle(RevealFileRequest request, CancellationToken cancellationToken)
    {
        session.Reveal(request.Id);
        return new ValueTask<InputSnapshot>(session.GetSnapshot());
    }
}
