using System.Text.Json;
using Obfy.Interop;
using Obfy.WebView;
using Shouldly;

namespace Obfy.Interop.Tests;

public class ApiEventAggregatorTests
{
    private sealed class CapturingWebView : IWebViewInterop
    {
        public List<string> Posted { get; } = [];
        public void PostWebMessageAsJson(string json) => Posted.Add(json);
    }

    [Fact]
    public void Publish_PostsEnvelope()
    {
        var webView = new CapturingWebView();
        var aggregator = new ApiEventAggregator(webView);

        aggregator.Publish("progress", new { FileName = "a.dll", Percent = 42 });

        webView.Posted.Count.ShouldBe(1);
        using var doc = JsonDocument.Parse(webView.Posted[0]);
        doc.RootElement.GetProperty("eventName").GetString().ShouldBe("progress");
        var data = doc.RootElement.GetProperty("data");
        data.GetProperty("fileName").GetString().ShouldBe("a.dll");
        data.GetProperty("percent").GetInt32().ShouldBe(42);
    }
}
