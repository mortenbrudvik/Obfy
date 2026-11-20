using Autofac;
using Mediator;
using Shouldly;
using Obfy.App.Features.App;
using Obfy.App.Features.Files;
using Obfy.Interop;
using Obfy.UI.DependencyInjection;

namespace Obfy.UI.Tests.Shell;

/// <summary>
/// Builds the container the way App.xaml.cs does (HybridBootstrap: Mediator populate + AppModule + HybridModule) and
/// resolves the bridge types. The container is built and resolved on an STA thread: the WebViewControl is a WPF control.
/// </summary>
public class HybridWiringTests
{
    private static T OnSta<T>(Func<T> work)
    {
        T result = default!;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { result = work(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        return result;
    }

    private static IContainer Build() => HybridBootstrap.BuildContainer();

    [Fact]
    public void Registrations_ResolveBridgeAndRegistry()
    {
        OnSta(() =>
        {
            using var container = Build();

            var bridge = container.Resolve<IApiBridge>();
            var registry = container.Resolve<ApiRegistry>();
            var events = container.Resolve<ApiEventAggregator>();

            bridge.ShouldNotBeNull();
            registry.ShouldNotBeNull();
            events.ShouldNotBeNull();
            container.Resolve<IApiBridge>().ShouldBeSameAs(bridge);
            container.Resolve<ApiRegistry>().ShouldBeSameAs(registry);
            container.Resolve<ApiEventAggregator>().ShouldBeSameAs(events);
            container.Resolve<ApiBridge>().ShouldBeSameAs(bridge);
            return true;
        });
    }

    [Fact]
    public async Task ApiInfo_ReturnsInformationalVersion()
    {
        using var container = Build();

        var info = await container.Resolve<IMediator>().Send(new AppInfoRequest());

        info.Version.ShouldNotBeNullOrWhiteSpace();
        info.Version.ShouldNotContain("+");
    }

    [Fact]
    public async Task DropPaths_IsHandledThroughMediator()
    {
        using var container = Build();

        var result = await container.Resolve<IMediator>().Send(new DropPathsRequest(["C:\\x\\a.dll"]));

        result.ShouldBe(Unit.Value);
    }

    [Fact]
    public async Task ApiBridge_AppInfo_RoundTripsThroughRegistry()
    {
        using var container = Build();
        ApiRegistrations.RegisterAll(container.Resolve<ApiRegistry>());

        var json = await container.Resolve<IApiBridge>().InvokeMethod(IpcNames.Services.App, IpcNames.Methods.AppInfo, "{}");

        json.ShouldContain("\"success\":true");
        json.ShouldContain("\"version\":\"");
    }

    [Fact]
    public async Task ReadyRequest_RaisesCoordinatorEvent()
    {
        using var container = Build();
        var coordinator = container.Resolve<AppReadyCoordinator>();
        var raised = 0;
        coordinator.Ready += () => raised++;

        await container.Resolve<IMediator>().Send(new ReadyRequest());

        raised.ShouldBe(1);
        coordinator.IsReady.ShouldBeTrue();
    }
}
