using Mediator;
using Obfy.App.Models;
using Obfy.App.Sessions;

namespace Obfy.App.Features.Preview;

/// <summary>
/// Decompiles a chosen output assembly to C# (<c>preview.get</c>). The decompile runs on a pool thread, so the call
/// returns a task that finishes when it does and holds no caller thread meanwhile. A failure comes back in
/// <see cref="PreviewResult.Error"/>.
/// </summary>
public record GetPreviewRequest(string? Path) : IRequest<PreviewResult>;

public class GetPreviewRequestHandler(ResultsStore results) : IRequestHandler<GetPreviewRequest, PreviewResult>
{
    public async ValueTask<PreviewResult> Handle(GetPreviewRequest request, CancellationToken cancellationToken) =>
        await results.LoadPreviewAsync(request.Path).ConfigureAwait(false);
}
