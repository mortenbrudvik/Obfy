using Mediator;
using Obfy.App.Models;
using Obfy.App.Sessions;

namespace Obfy.App.Features.Results;

/// <summary>
/// Asks for one window of the symbol tree (<c>results.symbols</c>): the roots, or the children of <paramref name="ParentId"/>,
/// filtered by <paramref name="Text"/>. The search runs on a pool thread over the model parsed once at load.
/// </summary>
public record QuerySymbolsRequest(string? Text = null, string? ParentId = null, int Offset = 0, int Limit = 200) : IRequest<SymbolsPage>;

public class QuerySymbolsRequestHandler(ResultsStore results) : IRequestHandler<QuerySymbolsRequest, SymbolsPage>
{
    public async ValueTask<SymbolsPage> Handle(QuerySymbolsRequest request, CancellationToken cancellationToken) =>
        await results.QueryAsync(request.Text, request.ParentId, request.Offset, request.Limit).ConfigureAwait(false);
}
