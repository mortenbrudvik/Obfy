namespace Obfy.WebView;

/// <summary>
/// Tracks whether the top-level WebView2 host object is attached and registers or removes it whenever the document
/// transitions into or out of the allowed origins.
/// </summary>
/// <remarks>
/// Top-level <c>CoreWebView2</c> has no origin-scoped host-object registration API (only <c>CoreWebView2Frame</c>
/// exposes <c>AddHostObjectToScript</c> with an origins allowlist), so this guard keeps the host object detached on
/// any non-allowed address and avoids calling <c>RemoveHostObjectFromScript</c> when nothing is attached.
/// </remarks>
internal sealed class HostObjectOriginGuard
{
    private readonly Action _attach;
    private readonly Action _detach;

    public HostObjectOriginGuard(Action attach, Action detach)
    {
        _attach = attach ?? throw new ArgumentNullException(nameof(attach));
        _detach = detach ?? throw new ArgumentNullException(nameof(detach));
    }

    /// <summary>Whether the host object is currently attached to the top-level WebView2 document.</summary>
    public bool IsAttached { get; private set; }

    /// <summary>
    /// Reconciles host-object attachment for <paramref name="uri"/> against <paramref name="allowedOrigins"/>.
    /// Attachment is permitted only when <paramref name="isErrorPage"/> is <c>false</c> and <paramref name="uri"/>
    /// matches an entry in <paramref name="allowedOrigins"/> (failing closed when <paramref name="allowedOrigins"/> is empty).
    /// </summary>
    public void ReconcileForUri(string? uri, IReadOnlySet<string> allowedOrigins, bool isErrorPage = false)
    {
        ArgumentNullException.ThrowIfNull(allowedOrigins);

        var shouldAttach = !isErrorPage && WebViewHandler.IsAllowedOriginCore(uri, allowedOrigins);
        if (shouldAttach)
        {
            if (IsAttached)
                return;

            _attach();
            IsAttached = true;
            return;
        }

        if (!IsAttached)
            return;

        _detach();
        IsAttached = false;
    }
}
