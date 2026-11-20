using System.Text.Json;
using Obfy.App.Sessions;
using Obfy.Interop;
using Obfy.WebView;
using Shouldly;

namespace Obfy.Interop.Tests;

public class SettingsEventForwarderTests
{
    private sealed class CapturingWebView : IWebViewInterop
    {
        public List<string> Posted { get; } = [];
        public void PostWebMessageAsJson(string json) => Posted.Add(json);
    }

    [Fact]
    public void SessionChange_PushesSettingsChangedWithTheSnapshot()
    {
        var webView = new CapturingWebView();
        var session = new ProtectionSession();
        _ = new SettingsEventForwarder(session, new ApiEventAggregator(webView));

        session.UpdateSetting("controlFlow.enabled", true);

        webView.Posted.Count.ShouldBe(1);
        using var doc = JsonDocument.Parse(webView.Posted[0]);
        doc.RootElement.GetProperty("eventName").GetString().ShouldBe(IpcNames.Events.SettingsChanged);
        doc.RootElement.GetProperty("eventName").GetString().ShouldBe("settings.changed");
        var data = doc.RootElement.GetProperty("data");
        data.GetProperty("level").GetString().ShouldBe("Custom");
        data.GetProperty("dirty").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public void NoOpUpdate_PushesNothing()
    {
        var webView = new CapturingWebView();
        var session = new ProtectionSession();
        _ = new SettingsEventForwarder(session, new ApiEventAggregator(webView));

        session.UpdateSetting("controlFlow.enabled", false);

        webView.Posted.ShouldBeEmpty();
    }
}
