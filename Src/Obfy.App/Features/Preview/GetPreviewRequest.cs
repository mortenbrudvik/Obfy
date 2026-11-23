using Mediator;
using Obfy.App.Models;
using Obfy.App.Sessions;

namespace Obfy.App.Features.Preview;

/// <summary>
/// Decompiles a chosen output assembly to C# (<c>preview.get</c>). The decompile runs on a pool thread, so the call
/// returns a task that finishes when it does and holds no caller thread meanwhile. A failure comes back in
/// <see cref="PreviewResult.Error"/>. Only a path from the results' preview list is decompiled; any other is refused with
/// <c>preview.unknownPath</c>.
/// </summary>
public record GetPreviewRequest(string? Path) : IRequest<PreviewResult>;

public class GetPreviewRequestHandler(ResultsStore results) : IRequestHandler<GetPreviewRequest, PreviewResult>
{
    public async ValueTask<PreviewResult> Handle(GetPreviewRequest request, CancellationToken cancellationToken) =>
        await results.LoadKnownPreviewAsync(request.Path).ConfigureAwait(false);
}
