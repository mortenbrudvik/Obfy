using Mediator;
using Obfy.App.Models;
using Obfy.App.Services;
using Obfy.App.Sessions;

namespace Obfy.App.Features.Files;

/// <summary>
/// Asks for files through the native picker and adds them (<c>files.pick</c>). The page never names a path. Cancelling
/// changes nothing. Answers with the snapshot.
/// </summary>
public record PickFilesRequest : IRequest<InputSnapshot>;

public class PickFilesRequestHandler(InputIntake intake, IFileDialogs dialogs, InputSession session)
    : IRequestHandler<PickFilesRequest, InputSnapshot>
{
    private const string Filter =
        "Assemblies, source, and solutions (*.dll;*.exe;*.cs;*.sln;*.slnx;*.csproj;*.vbproj;*.fsproj)|*.dll;*.exe;*.cs;*.sln;*.slnx;*.csproj;*.vbproj;*.fsproj"
        + "|Assembly Files (*.dll;*.exe)|*.dll;*.exe|C# Files (*.cs)|*.cs|All Files (*.*)|*.*";

    public async ValueTask<InputSnapshot> Handle(PickFilesRequest request, CancellationToken cancellationToken)
    {
        var picked = await dialogs.PickFilesAsync("Select Assembly Files", Filter, multi: true).ConfigureAwait(false);
        if (picked.Length > 0)
            await intake.AddPathsAsync(picked, cancellationToken);

        return session.GetSnapshot();
    }
}
