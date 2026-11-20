using Microsoft.Web.WebView2.Core;
using Obfy.WebView;
using Shouldly;

namespace Obfy.Interop.Tests;

public class WebViewHandlerTests
{
    private static readonly string[] Allowed = ["https://obfy.localassets.invalid", "http://localhost:5173"];

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
}
