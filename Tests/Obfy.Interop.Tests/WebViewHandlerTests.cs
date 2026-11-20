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

    [Fact]
    public void DefaultHostName_IsObfyLocalAssets() =>
        WebViewHandler.DefaultHostName.ShouldBe("obfy.localassets.invalid");
}
