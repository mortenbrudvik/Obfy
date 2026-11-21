using System.IO;
using System.Text.Json;
using Mediator;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Obfy.App;
using Obfy.Interop;
using Shouldly;

namespace Obfy.Interop.Tests;

public class ApiBridgeTests
{
    private sealed record FakeRequest(string Name) : IRequest<FakeResponse>;

    private sealed record FakeResponse(string Greeting, int Count);

    private static (ApiBridge Bridge, Mock<IMediator> Mediator) CreateBridge()
    {
        var mediator = new Mock<IMediator>();
        var registry = new ApiRegistry();
        registry.Register<FakeRequest>("fake", "greet");
        var bridge = new ApiBridge(mediator.Object, registry, new IpcLogger(NullLogger<IpcLogger>.Instance));
        return (bridge, mediator);
    }

    private static void SetupSend(Mock<IMediator> mediator, Func<FakeRequest, ValueTask<object?>> handler) =>
        mediator.Setup(m => m.Send(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Returns((object request, CancellationToken _) => handler((FakeRequest)request));

    [Fact]
    public async Task InvokeMethod_UnknownApi_ReturnsError()
    {
        var (bridge, _) = CreateBridge();

        var json = await bridge.InvokeMethod("nope", "missing", "{}");

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("success").GetBoolean().ShouldBeFalse();
        doc.RootElement.GetProperty("error").GetProperty("code").GetString().ShouldBe("UnknownApi");
    }

    [Fact]
    public async Task InvokeMethod_Success_WrapsData()
    {
        var (bridge, mediator) = CreateBridge();
        SetupSend(mediator, r => new ValueTask<object?>(new FakeResponse($"Hello {r.Name}", 3)));

        var json = await bridge.InvokeMethod("fake", "greet", """{"name":"Ada"}""");

        json.ShouldContain("\"success\":true");
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("success").GetBoolean().ShouldBeTrue();
        var data = doc.RootElement.GetProperty("data");
        data.GetProperty("greeting").GetString().ShouldBe("Hello Ada");
        data.GetProperty("count").GetInt32().ShouldBe(3);
    }

    [Fact]
    public async Task InvokeMethod_DomainException_PassesCode()
    {
        var (bridge, mediator) = CreateBridge();
        SetupSend(mediator, _ => throw new ObfyApiException("NotFound", "No such file."));

        var json = await bridge.InvokeMethod("fake", "greet", """{"name":"Ada"}""");

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("success").GetBoolean().ShouldBeFalse();
        var error = doc.RootElement.GetProperty("error");
        error.GetProperty("code").GetString().ShouldBe("NotFound");
        error.GetProperty("message").GetString().ShouldBe("No such file.");
    }

    [Theory]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    public async Task InvokeMethod_UserFixableException_IsBadRequest(Type exceptionType)
    {
        var (bridge, mediator) = CreateBridge();
        SetupSend(mediator, _ => throw (Exception)Activator.CreateInstance(exceptionType, "Disk is full.")!);

        var json = await bridge.InvokeMethod("fake", "greet", """{"name":"Ada"}""");

        using var doc = JsonDocument.Parse(json);
        var error = doc.RootElement.GetProperty("error");
        error.GetProperty("code").GetString().ShouldBe("BadRequest");
        error.GetProperty("message").GetString()!.ShouldContain("Disk is full.");
    }

    [Fact]
    public async Task InvokeMethod_UnknownException_IsGeneric()
    {
        var (bridge, mediator) = CreateBridge();
        SetupSend(mediator, _ => throw new InvalidOperationException("secret internal detail"));

        var json = await bridge.InvokeMethod("fake", "greet", """{"name":"Ada"}""");

        json.ShouldNotContain("secret internal detail");
        using var doc = JsonDocument.Parse(json);
        var error = doc.RootElement.GetProperty("error");
        error.GetProperty("code").GetString().ShouldBe("Internal");
        error.GetProperty("message").GetString().ShouldNotBeNullOrWhiteSpace();
    }
}
