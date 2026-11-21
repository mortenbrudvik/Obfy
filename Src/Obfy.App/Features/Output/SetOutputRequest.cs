using Mediator;
using Obfy.App.Models;
using Obfy.App.Sessions;

namespace Obfy.App.Features.Output;

/// <summary>
/// Sets the output folder from the text the user typed (<c>output.set</c>); empty clears it. The path is checked and made
/// absolute; one that cannot be a folder is refused with <c>output.invalidPath</c> or <c>output.isFile</c>. Refused with
/// <c>run.blocked</c> while a run goes. Answers with the snapshot.
/// </summary>
public record SetOutputRequest(string Folder) : IRequest<InputSnapshot>;

public class SetOutputRequestHandler(InputSession session, RunCoordinator run) : IRequestHandler<SetOutputRequest, InputSnapshot>
{
    public ValueTask<InputSnapshot> Handle(SetOutputRequest request, CancellationToken cancellationToken)
    {
        run.EnsureNotRunning();
        session.SetOutputFolder(request.Folder);
        return new ValueTask<InputSnapshot>(session.GetSnapshot());
    }
}
