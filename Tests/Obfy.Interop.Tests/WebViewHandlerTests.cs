using Microsoft.Web.WebView2.Core;
using Obfy.WebView;
using Shouldly;

namespace Obfy.Interop.Tests;

public class WebViewHandlerTests
{
    private static readonly string[] Allowed = ["https://obfy.localassets.invalid", "http://localhost:5173"];
    private static readonly HashSet<string> AllowedSet = new(Allowed, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void IsFilesDroppedMessage_OnlyTheExactToken_True()
    {
        WebViewHandler.IsFilesDroppedMessage("files.dropped").ShouldBeTrue();
        WebViewHandler.IsFilesDroppedMessage(WebViewHandler.FilesDroppedMessage).ShouldBeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Files.Dropped")]
    [InlineData(" files.dropped")]
    [InlineData("files.dropped ")]
    [InlineData("files.pick")]
    [InlineData("\"files.dropped\"")]
    public void IsFilesDroppedMessage_AnythingElse_False(string? message)
    {
        WebViewHandler.IsFilesDroppedMessage(message).ShouldBeFalse();
    }

    [Fact]
    public void IsAllowedOrigin_ExactMatch_True()
    {
        WebViewHandler.IsAllowedOrigin(new Uri("https://obfy.localassets.invalid/index.html?x=1"), Allowed).ShouldBeTrue();
        WebViewHandler.IsAllowedOrigin(new Uri("http://localhost:5173/"), Allowed).ShouldBeTrue();
    }

    [Fact]
    public void IsAllowedOrigin_OtherPort_False()
    {
        WebViewHandler.IsAllowedOrigin(new Uri("http://localhost:5174/"), Allowed).ShouldBeFalse();
        WebViewHandler.IsAllowedOrigin(new Uri("https://obfy.localassets.invalid:8443/"), Allowed).ShouldBeFalse();
    }

    [Fact]
    public void IsAllowedOrigin_OtherHostOrScheme_False()
    {
        WebViewHandler.IsAllowedOrigin(new Uri("https://evil.example/"), Allowed).ShouldBeFalse();
        WebViewHandler.IsAllowedOrigin(new Uri("http://obfy.localassets.invalid/"), Allowed).ShouldBeFalse();
        WebViewHandler.IsAllowedOrigin(new Uri("https://localhost:5173/"), Allowed).ShouldBeFalse();
    }

    [Fact]
    public void IsAllowedOrigin_NoHostOrEmptyAllowlist_False()
    {
        WebViewHandler.IsAllowedOrigin(new Uri("about:blank"), Allowed).ShouldBeFalse();
        WebViewHandler.IsAllowedOrigin(new Uri("file:///C:/x.html"), Allowed).ShouldBeFalse();
        WebViewHandler.IsAllowedOrigin(new Uri("https://obfy.localassets.invalid/"), []).ShouldBeFalse();
    }

    [Fact]
    public void ShouldShowContextMenu_Editable_True() =>
        WebViewHandler.ShouldShowContextMenu(isEditable: true, hasSelection: false).ShouldBeTrue();

    [Fact]
    public void ShouldShowContextMenu_Selection_True() =>
        WebViewHandler.ShouldShowContextMenu(isEditable: false, hasSelection: true).ShouldBeTrue();

    [Fact]
    public void ShouldShowContextMenu_Neither_False() =>
        WebViewHandler.ShouldShowContextMenu(isEditable: false, hasSelection: false).ShouldBeFalse();

    [Theory]
    [InlineData(CoreWebView2ProcessFailedKind.RenderProcessExited)]
    [InlineData(CoreWebView2ProcessFailedKind.RenderProcessUnresponsive)]
    public void RespondTo_RendererFailure_ReloadsUpToMaxReloads(CoreWebView2ProcessFailedKind kind)
    {
        // reloadsSoFar 0, 1, 2 are the 1st, 2nd and 3rd failures: each is reloaded.
        for (var reloads = 0; reloads < WebViewHandler.MaxReloads; reloads++)
            WebViewHandler.RespondTo(kind, reloads).ShouldBe(ProcessFailureResponse.Reload);

        // The 4th failure (3 reloads already spent) gives up.
        WebViewHandler.RespondTo(kind, WebViewHandler.MaxReloads).ShouldBe(ProcessFailureResponse.GiveUp);
        WebViewHandler.RespondTo(kind, WebViewHandler.MaxReloads + 1).ShouldBe(ProcessFailureResponse.GiveUp);
    }

    [Fact]
    public void RespondTo_MaxReloads_IsThree() => WebViewHandler.MaxReloads.ShouldBe(3);

    [Fact]
    public void RespondTo_BrowserProcessExited_GivesUpImmediately() =>
        WebViewHandler.RespondTo(CoreWebView2ProcessFailedKind.BrowserProcessExited, 0).ShouldBe(ProcessFailureResponse.GiveUp);

    [Theory]
    [InlineData(CoreWebView2ProcessFailedKind.GpuProcessExited)]
    [InlineData(CoreWebView2ProcessFailedKind.FrameRenderProcessExited)]
    [InlineData(CoreWebView2ProcessFailedKind.UtilityProcessExited)]
    public void RespondTo_SelfRecoveringProcess_DoesNothing(CoreWebView2ProcessFailedKind kind) =>
        WebViewHandler.RespondTo(kind, 0).ShouldBe(ProcessFailureResponse.None);

    [Fact]
    public void DefaultHostName_IsObfyLocalAssets() =>
        WebViewHandler.DefaultHostName.ShouldBe("obfy.localassets.invalid");

    [Fact]
    public void HostObjectOriginGuard_AllowedInitialUri_AttachesOnce()
    {
        var attachCount = 0;
        var detachCount = 0;
        var guard = new HostObjectOriginGuard(() => attachCount++, () => detachCount++);

        guard.IsAttached.ShouldBeFalse();

        guard.ReconcileForUri("https://obfy.localassets.invalid/index.html", AllowedSet);

        attachCount.ShouldBe(1);
        detachCount.ShouldBe(0);
        guard.IsAttached.ShouldBeTrue();
    }

    [Fact]
    public void HostObjectOriginGuard_IdempotentReconcile_DoesNotReattach()
    {
        var attachCount = 0;
        var detachCount = 0;
        var guard = new HostObjectOriginGuard(() => attachCount++, () => detachCount++);

        guard.ReconcileForUri("https://obfy.localassets.invalid/index.html", AllowedSet);
        guard.ReconcileForUri("https://obfy.localassets.invalid/other?x=1", AllowedSet);
        guard.ReconcileForUri("http://localhost:5173/", AllowedSet);

        attachCount.ShouldBe(1);
        detachCount.ShouldBe(0);
        guard.IsAttached.ShouldBeTrue();
    }

    [Theory]
    [InlineData("about:blank")]
    [InlineData("about:srcdoc")]
    [InlineData("edge-error://navigation/")]
    [InlineData("chrome-error://chromewebdata/")]
    [InlineData("file:///C:/secret.html")]
    [InlineData("https://evil.example/")]
    [InlineData("")]
    [InlineData(null)]
    public void HostObjectOriginGuard_DisallowedInitialUri_DoesNotAttachOrDetach(string? uri)
    {
        var attachCount = 0;
        var detachCount = 0;
        var guard = new HostObjectOriginGuard(() => attachCount++, () => detachCount++);

        guard.ReconcileForUri(uri, AllowedSet);

        attachCount.ShouldBe(0);
        detachCount.ShouldBe(0);
        guard.IsAttached.ShouldBeFalse();
    }

    [Theory]
    [InlineData("about:blank")]
    [InlineData("edge-error://navigation/")]
    [InlineData("chrome-error://chromewebdata/")]
    [InlineData("file:///C:/secret.html")]
    [InlineData("https://evil.example/")]
    [InlineData("")]
    [InlineData(null)]
    public void HostObjectOriginGuard_TransitionFromAllowedToDisallowed_DetachesOnce(string? disallowedUri)
    {
        var attachCount = 0;
        var detachCount = 0;
        var guard = new HostObjectOriginGuard(() => attachCount++, () => detachCount++);

        guard.ReconcileForUri("https://obfy.localassets.invalid/index.html", AllowedSet);
        guard.ReconcileForUri(disallowedUri, AllowedSet);

        attachCount.ShouldBe(1);
        detachCount.ShouldBe(1);
        guard.IsAttached.ShouldBeFalse();
    }

    [Fact]
    public void HostObjectOriginGuard_RepeatDisallowed_DoesNotDoubleDetach()
    {
        var attachCount = 0;
        var detachCount = 0;
        var guard = new HostObjectOriginGuard(() => attachCount++, () => detachCount++);

        guard.ReconcileForUri("http://localhost:5173/", AllowedSet);
        guard.ReconcileForUri("about:blank", AllowedSet);
        guard.ReconcileForUri("edge-error://navigation/", AllowedSet);
        guard.ReconcileForUri("https://evil.example/", AllowedSet);
        guard.ReconcileForUri(null, AllowedSet);

        attachCount.ShouldBe(1);
        detachCount.ShouldBe(1);
        guard.IsAttached.ShouldBeFalse();
    }

    [Fact]
    public void HostObjectOriginGuard_TransitionBackToAllowed_Reattaches()
    {
        var attachCount = 0;
        var detachCount = 0;
        var guard = new HostObjectOriginGuard(() => attachCount++, () => detachCount++);

        guard.ReconcileForUri("https://obfy.localassets.invalid/index.html", AllowedSet);
        guard.ReconcileForUri("about:blank", AllowedSet);
        guard.ReconcileForUri("https://obfy.localassets.invalid/index.html", AllowedSet);
        guard.ReconcileForUri("https://obfy.localassets.invalid/index.html", AllowedSet);

        attachCount.ShouldBe(2);
        detachCount.ShouldBe(1);
        guard.IsAttached.ShouldBeTrue();
    }

    [Fact]
    public void HostObjectOriginGuard_EmptyAllowedOrigins_RefusesToAttach()
    {
        var attachCount = 0;
        var detachCount = 0;
        var guard = new HostObjectOriginGuard(() => attachCount++, () => detachCount++);
        var emptyAllowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        guard.ReconcileForUri("https://obfy.localassets.invalid/index.html", emptyAllowed);
        guard.ReconcileForUri("https://anything.example/", emptyAllowed);
        guard.ReconcileForUri("about:blank", emptyAllowed);
        guard.ReconcileForUri(null, emptyAllowed);

        attachCount.ShouldBe(0);
        detachCount.ShouldBe(0);
        guard.IsAttached.ShouldBeFalse();
    }

    [Fact]
    public void HostObjectOriginGuard_AttachedThenEmptyAllowedOrigins_Detaches()
    {
        var attachCount = 0;
        var detachCount = 0;
        var guard = new HostObjectOriginGuard(() => attachCount++, () => detachCount++);
        var emptyAllowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        guard.ReconcileForUri("https://obfy.localassets.invalid/index.html", AllowedSet);
        guard.IsAttached.ShouldBeTrue();

        guard.ReconcileForUri("https://obfy.localassets.invalid/index.html", emptyAllowed);

        attachCount.ShouldBe(1);
        detachCount.ShouldBe(1);
        guard.IsAttached.ShouldBeFalse();
    }

    [Fact]
    public void HostObjectOriginGuard_ErrorPageWithAllowedUri_WhenDetached_RefusesToAttach()
    {
        var attachCount = 0;
        var detachCount = 0;
        var guard = new HostObjectOriginGuard(() => attachCount++, () => detachCount++);

        guard.ReconcileForUri("https://obfy.localassets.invalid/index.html", AllowedSet, isErrorPage: true);

        attachCount.ShouldBe(0);
        detachCount.ShouldBe(0);
        guard.IsAttached.ShouldBeFalse();
    }

    [Fact]
    public void HostObjectOriginGuard_ErrorPageWithAllowedUri_DetachesAndReattachesWhenRecovered()
    {
        var attachCount = 0;
        var detachCount = 0;
        var guard = new HostObjectOriginGuard(() => attachCount++, () => detachCount++);

        guard.ReconcileForUri("https://obfy.localassets.invalid/index.html", AllowedSet);
        guard.IsAttached.ShouldBeTrue();

        guard.ReconcileForUri("https://obfy.localassets.invalid/index.html", AllowedSet, isErrorPage: true);
        guard.ReconcileForUri("https://obfy.localassets.invalid/index.html", AllowedSet, isErrorPage: true);

        attachCount.ShouldBe(1);
        detachCount.ShouldBe(1);
        guard.IsAttached.ShouldBeFalse();

        guard.ReconcileForUri("https://obfy.localassets.invalid/index.html", AllowedSet, isErrorPage: false);

        attachCount.ShouldBe(2);
        detachCount.ShouldBe(1);
        guard.IsAttached.ShouldBeTrue();
    }

    [Fact]
    public void NormalizeAllowedOrigins_NormalizesValidOriginsAndDropsInvalid()
    {
        var normalized = WebViewHandler.NormalizeAllowedOrigins([
            "https://OBFY.LOCALASSETS.INVALID/index.html?x=1",
            "https://obfy.localassets.invalid:443/other",
            "HTTP://LOCALHOST:5173/sub",
            "about:blank",
            "file:///C:/x.html",
            "not-a-uri",
            "",
        ]);

        normalized.Count.ShouldBe(2);
        normalized.ShouldContain("https://obfy.localassets.invalid:443");
        normalized.ShouldContain("http://localhost:5173");
    }

    [Fact]
    public void OriginChecks_AcceptBothNormalizedAndRawUrlSets()
    {
        string[] rawUrls = ["https://obfy.localassets.invalid/index.html?x=1", "http://localhost:5173/app"];
        var rawSet = new HashSet<string>(rawUrls, StringComparer.OrdinalIgnoreCase);
        var normalizedSet = WebViewHandler.NormalizeAllowedOrigins(rawUrls);

        foreach (var set in new[] { rawSet, normalizedSet })
        {
            WebViewHandler.ShouldAllowSubframeNavigation("https://obfy.localassets.invalid/frame.html", set).ShouldBeTrue();
            WebViewHandler.ShouldAllowSubframeNavigation("http://localhost:5173/other", set).ShouldBeTrue();
            WebViewHandler.ShouldAllowSubframeNavigation("https://evil.example/", set).ShouldBeFalse();
            WebViewHandler.ShouldAllowSubframeNavigation("http://obfy.localassets.invalid/", set).ShouldBeFalse();

            var attachCount = 0;
            var guard = new HostObjectOriginGuard(() => attachCount++, () => { });
            guard.ReconcileForUri("https://obfy.localassets.invalid/other.html", set);
            attachCount.ShouldBe(1);
            guard.IsAttached.ShouldBeTrue();
        }

        WebViewHandler.IsAllowedOrigin("https://obfy.localassets.invalid/other.html", rawUrls).ShouldBeTrue();
        WebViewHandler.IsAllowedOrigin("https://evil.example/", rawUrls).ShouldBeFalse();
    }

    [Theory]
    [InlineData("https://obfy.localassets.invalid/frame.html")]
    [InlineData("https://obfy.localassets.invalid/")]
    [InlineData("http://localhost:5173/sub?x=1")]
    public void ShouldAllowSubframeNavigation_AllowedOrigin_True(string uri)
    {
        WebViewHandler.ShouldAllowSubframeNavigation(uri, AllowedSet).ShouldBeTrue();
    }

    [Theory]
    [InlineData("about:blank")]
    [InlineData("about:srcdoc")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/x.html")]
    [InlineData("file://server/share/x.html")]
    [InlineData("edge-error://navigation/")]
    [InlineData("chrome-error://chromewebdata/")]
    [InlineData("blob:https://obfy.localassets.invalid/123")]
    [InlineData("https://evil.example/")]
    [InlineData("http://obfy.localassets.invalid/")]
    [InlineData("https://obfy.localassets.invalid:8443/")]
    [InlineData("http://localhost:5174/")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("not-a-uri")]
    public void ShouldAllowSubframeNavigation_DisallowedOrNonOriginUri_False(string? uri)
    {
        WebViewHandler.ShouldAllowSubframeNavigation(uri, AllowedSet).ShouldBeFalse();
    }

    [Fact]
    public void ShouldAllowSubframeNavigation_EmptyAllowedOrigins_False()
    {
        var emptyAllowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        WebViewHandler.ShouldAllowSubframeNavigation("https://obfy.localassets.invalid/frame.html", emptyAllowed).ShouldBeFalse();
    }
}
