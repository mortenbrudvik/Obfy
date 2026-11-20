using Mediator;
using Obfy.App.Models;
using Obfy.App.Sessions;

namespace Obfy.App.Features.Files;

/// <summary>
/// Opens the folder the output went to in the file browser (<c>files.openOutput</c>). No folder yet is a notice in the
/// snapshot. Answers with the snapshot.
/// </summary>
public record OpenOutputRequest : IRequest<InputSnapshot>;

public class OpenOutputRequestHandler(InputSession session) : IRequestHandler<OpenOutputRequest, InputSnapshot>
{
    public ValueTask<InputSnapshot> Handle(OpenOutputRequest request, CancellationToken cancellationToken)
    {
        session.OpenOutputFolder();
        return new ValueTask<InputSnapshot>(session.GetSnapshot());
    }
}
