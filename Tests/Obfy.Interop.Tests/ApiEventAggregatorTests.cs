using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
        var aggregator = new ApiEventAggregator(webView, new IpcLogger(NullLogger<IpcLogger>.Instance));

        aggregator.Publish("progress", new { FileName = "a.dll", Percent = 42 });

        webView.Posted.Count.ShouldBe(1);
        using var doc = JsonDocument.Parse(webView.Posted[0]);
        doc.RootElement.GetProperty("eventName").GetString().ShouldBe("progress");
        var data = doc.RootElement.GetProperty("data");
        data.GetProperty("fileName").GetString().ShouldBe("a.dll");
        data.GetProperty("percent").GetInt32().ShouldBe(42);
    }

    private sealed class CapturingLogger : ILogger<IpcLogger>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }

    [Fact]
    public void Publish_LogsTheEventAtDebug_WithItsNameAndSizeButNotThePayload()
    {
        var logger = new CapturingLogger();
        var webView = new CapturingWebView();
        var aggregator = new ApiEventAggregator(webView, new IpcLogger(logger));

        aggregator.Publish("files.changed", new { Path = "C:/secret/customer.dll" });

        var entry = logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Debug);
        entry.Message.ShouldContain("files.changed");
        entry.Message.ShouldContain($"{webView.Posted[0].Length} chars");
        entry.Message.ShouldNotContain("customer.dll");
        entry.Message.ShouldNotContain("secret");
    }
}
