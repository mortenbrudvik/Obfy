using System.Text.Json;
using Obfy.App;
using Obfy.App.Sessions;
using Microsoft.Extensions.Logging.Abstractions;
using Obfy.Interop;
using Obfy.WebView;
using Shouldly;

namespace Obfy.Interop.Tests;

public class ApiEventPublisherTests
{
    private sealed class CapturingWebView : IWebViewInterop
    {
        public List<string> Posted { get; } = [];
        public void PostWebMessageAsJson(string json) => Posted.Add(json);
    }

    private static ApiEventPublisher PublisherOver(CapturingWebView webView) =>
        new(new Lazy<ApiEventAggregator>(() => new ApiEventAggregator(webView, new IpcLogger(NullLogger<IpcLogger>.Instance))));

    [Fact]
    public void Publish_GoesThroughTheAggregatorAsAnEnvelope()
    {
        var webView = new CapturingWebView();

        PublisherOver(webView).Publish("thing.happened", new { Count = 2 });

        using var doc = JsonDocument.Parse(webView.Posted.ShouldHaveSingleItem());
        doc.RootElement.GetProperty("eventName").GetString().ShouldBe("thing.happened");
        doc.RootElement.GetProperty("data").GetProperty("count").GetInt32().ShouldBe(2);
    }

    [Fact]
    public void TheAggregatorIsNotBuiltUntilTheFirstPublish()
    {
        var built = 0;
        var publisher = new ApiEventPublisher(new Lazy<ApiEventAggregator>(() =>
        {
            built++;
            return new ApiEventAggregator(new CapturingWebView(), new IpcLogger(NullLogger<IpcLogger>.Instance));
        }));

        built.ShouldBe(0);
        publisher.Publish("a", null);
        publisher.Publish("b", null);
        built.ShouldBe(1);
    }

    [Fact]
    public void ASessionChange_PushesSettingsChanged_AndIpcNamesUsesTheAppEventName()
    {
        var webView = new CapturingWebView();
        var session = new ProtectionSession(PublisherOver(webView));

        session.UpdateSetting("controlFlow.enabled", true);

        using var doc = JsonDocument.Parse(webView.Posted.ShouldHaveSingleItem());
        doc.RootElement.GetProperty("eventName").GetString().ShouldBe(IpcNames.Events.SettingsChanged);
        IpcNames.Events.SettingsChanged.ShouldBe(AppEvents.SettingsChanged);
        var data = doc.RootElement.GetProperty("data");
        data.GetProperty("level").GetString().ShouldBe("Custom");
        data.GetProperty("revision").GetInt64().ShouldBeGreaterThan(0);
    }

    [Fact]
    public void NoOpUpdate_PushesNothing()
    {
        var webView = new CapturingWebView();
        var session = new ProtectionSession(PublisherOver(webView));

        session.UpdateSetting("controlFlow.enabled", false);

        webView.Posted.ShouldBeEmpty();
    }
}
