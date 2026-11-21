using System.IO;
using System.Text.Json;
using Mediator;
using Microsoft.Extensions.Logging;
using Moq;
using Obfy.App;
using Obfy.Interop;
using Shouldly;

namespace Obfy.Interop.Tests;

/// <summary>
/// Regression tests for the bridge's logging and error mapping: what the page sends never reaches the log file raw, no
/// exception message is logged (it can hold a path), and only a bad request is a BadRequest.
/// </summary>
public class ApiBridgeReviewTests
{
    private sealed record FakeRequest(string Name) : IRequest<FakeResponse>;

    private sealed record FakeResponse(string Greeting);

    private sealed class CapturingLogger : ILogger<IpcLogger>
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception), exception));
    }

    private static (ApiBridge Bridge, Mock<IMediator> Mediator, CapturingLogger Log) CreateBridge()
    {
        var mediator = new Mock<IMediator>();
        var registry = new ApiRegistry();
        registry.Register<FakeRequest>("fake", "greet");
        var log = new CapturingLogger();
        return (new ApiBridge(mediator.Object, registry, new IpcLogger(log)), mediator, log);
    }

    private static void SetupThrow(Mock<IMediator> mediator, Exception exception) =>
        mediator.Setup(m => m.Send(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Returns(() => throw exception);

    private static string CodeOf(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("error").GetProperty("code").GetString()!;
    }

    // ---- logging ----

    [Fact]
    public async Task AnInternalError_IsLoggedByCodeAndExceptionType_NeverItsMessageOrObject()
    {
        var (bridge, mediator, log) = CreateBridge();
        SetupThrow(mediator, new InvalidOperationException(@"could not open C:\Users\someone\secret.dll"));

        var json = await bridge.InvokeMethod("fake", "greet", """{"name":"Ada"}""");

        CodeOf(json).ShouldBe("Internal");
        log.Entries.ShouldContain(e => e.Level == LogLevel.Error && e.Message.Contains("Internal") && e.Message.Contains(nameof(InvalidOperationException)));
        log.Entries.ShouldAllBe(e => e.Exception == null && !e.Message.Contains("secret") && !e.Message.Contains("someone"));
    }

    [Fact]
    public async Task APageSuppliedName_NeverReachesTheLogRaw()
    {
        var (bridge, _, log) = CreateBridge();
        const string forged = "x\r\n2026-01-01 12:00:00 ERROR forged line from the page";

        await bridge.InvokeMethod(forged, forged, "{}");

        log.Entries.ShouldNotBeEmpty();
        log.Entries.ShouldAllBe(e => !e.Message.Contains('\n') && !e.Message.Contains('\r') && !e.Message.Contains("forged line"));
    }

    [Fact]
    public async Task AVeryLongPageSuppliedName_IsCutInTheLog()
    {
        var (bridge, _, log) = CreateBridge();
        var name = new string('a', 5000);

        await bridge.InvokeMethod(name, "greet", "{}");

        log.Entries.ShouldAllBe(e => e.Message.Length < 400);
    }

    [Fact]
    public async Task ARegisteredCall_IsStillLoggedByItsName()
    {
        var (bridge, mediator, log) = CreateBridge();
        mediator.Setup(m => m.Send(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<object?>(new FakeResponse("hi")));

        await bridge.InvokeMethod("fake", "greet", """{"name":"Ada"}""");

        log.Entries.ShouldContain(e => e.Message.Contains("fake.greet"));
    }

    // ---- error mapping ----

    [Theory]
    [InlineData(typeof(ArgumentException))]
    [InlineData(typeof(ArgumentNullException))]
    [InlineData(typeof(JsonException))]
    public async Task AnExceptionFromAHandler_ThatIsNotAnIoError_IsInternal_AndReachesTheErrorLog(Type exceptionType)
    {
        var (bridge, mediator, log) = CreateBridge();
        SetupThrow(mediator, (Exception)Activator.CreateInstance(exceptionType, "host bug")!);

        var json = await bridge.InvokeMethod("fake", "greet", """{"name":"Ada"}""");

        CodeOf(json).ShouldBe("Internal");
        json.ShouldNotContain("host bug");
        log.Entries.ShouldContain(e => e.Level == LogLevel.Error);
    }

    [Theory]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    public async Task AFileSystemError_FromAHandler_IsStillBadRequest(Type exceptionType)
    {
        var (bridge, mediator, _) = CreateBridge();
        SetupThrow(mediator, (Exception)Activator.CreateInstance(exceptionType, "Disk is full.")!);

        var json = await bridge.InvokeMethod("fake", "greet", """{"name":"Ada"}""");

        CodeOf(json).ShouldBe("BadRequest");
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("null")]
    [InlineData("[1,2]")]
    public async Task ARequestThatCannotBeRead_IsBadRequest_NotInternal(string jsonParams)
    {
        var (bridge, _, log) = CreateBridge();

        var json = await bridge.InvokeMethod("fake", "greet", jsonParams);

        CodeOf(json).ShouldBe("BadRequest");
        log.Entries.ShouldNotContain(e => e.Level == LogLevel.Error);
    }
}
