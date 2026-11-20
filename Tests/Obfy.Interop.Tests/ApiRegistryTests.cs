using Mediator;
using Obfy.Interop;
using Shouldly;

namespace Obfy.Interop.Tests;

public class ApiRegistryTests
{
    private sealed record FakeRequest(string Name) : IRequest<string>;

    [Fact]
    public void Register_ThenGetRequestType_RoundTrips()
    {
        var registry = new ApiRegistry();
        registry.Register<FakeRequest>("svc", "do");

        registry.GetRequestType("svc", "do").ShouldBe(typeof(FakeRequest));
    }

    [Fact]
    public void GetRequestType_Unknown_ReturnsNull()
    {
        new ApiRegistry().GetRequestType("svc", "missing").ShouldBeNull();
    }
}
