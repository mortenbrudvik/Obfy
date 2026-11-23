using Mediator;
using Microsoft.Extensions.Logging;

namespace Obfy.App.Features.App;

/// <summary>
/// Records an unhandled page render error reported by the frontend ErrorBoundary (<c>app.reportError</c>).
/// Only short sanitized screen and error-type identifiers are accepted and logged; never messages, stacks, or paths.
/// </summary>
public sealed record ReportPageErrorRequest(string? Screen, string? ErrorType) : IRequest<Unit>;

public sealed class ReportPageErrorRequestHandler(ILogger<ReportPageErrorRequestHandler> logger)
    : IRequestHandler<ReportPageErrorRequest, Unit>
{
    private const int MaxIdentifierLength = 64;

    public ValueTask<Unit> Handle(ReportPageErrorRequest request, CancellationToken cancellationToken)
    {
        var screen = SanitizeIdentifier(request.Screen);
        var errorType = SanitizeIdentifier(request.ErrorType);
        logger.LogError("Frontend page error on {Screen} ({ErrorType})", screen, errorType);
        return Unit.ValueTask;
    }

    internal static string SanitizeIdentifier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "unknown";

        var trimmed = value.Trim();
        var length = Math.Min(trimmed.Length, MaxIdentifierLength);
        return string.Create(length, trimmed, static (span, source) =>
        {
            for (var i = 0; i < span.Length; i++)
                span[i] = char.IsAsciiLetterOrDigit(source[i]) || source[i] is '.' or '_' or '-' ? source[i] : '?';
        });
    }
}
