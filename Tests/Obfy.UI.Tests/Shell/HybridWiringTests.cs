using System.IO;
using Autofac;
using Mediator;
using Shouldly;
using Obfy.App.Features.App;
using Obfy.App.Features.Files;
using Obfy.App.Sessions;
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

    [Fact]
    public void ApiBridge_SettingsUpdate_ReachesTheSharedSession()
    {
        // On the UI thread, as in the app: the web view a change publishes through is built (and disposed) there.
        OnSta(() =>
        {
            using var container = Build();
            ApiRegistrations.RegisterAll(container.Resolve<ApiRegistry>());
            var bridge = container.Resolve<IApiBridge>();
            container.Resolve<ApiEventAggregator>();

            var updated = bridge.InvokeMethod(
                IpcNames.Services.Settings, IpcNames.Methods.SettingsUpdate, """{"patch":{"controlFlow.enabled":true}}""").GetAwaiter().GetResult();
            var read = bridge.InvokeMethod(IpcNames.Services.Settings, IpcNames.Methods.SettingsGet, "{}").GetAwaiter().GetResult();
            var bad = bridge.InvokeMethod(
                IpcNames.Services.Settings, IpcNames.Methods.SettingsUpdate, """{"patch":{"nope.nothing":1}}""").GetAwaiter().GetResult();

            updated.ShouldContain("\"success\":true");
            updated.ShouldContain("\"level\":\"Custom\"");
            read.ShouldContain("\"level\":\"Custom\"");
            bad.ShouldContain("\"success\":false");
            bad.ShouldContain("settings.unknownField");
            container.Resolve<ProtectionSession>().Level.ShouldBe(Obfy.Core.Models.ObfuscationLevel.Custom);
            return true;
        });
    }

    [Fact]
    public void ApiBridge_FilesEndpoints_ReachTheSharedInputSession()
    {
        // On the UI thread, as in the app: a change publishes files.changed, which builds the web view there.
        OnSta(() =>
        {
            using var container = Build();
            ApiRegistrations.RegisterAll(container.Resolve<ApiRegistry>());
            var bridge = container.Resolve<IApiBridge>();
            container.Resolve<ApiEventAggregator>();
            var dll = Path.Combine(Path.GetTempPath(), $"wiring-{Guid.NewGuid():N}.dll");
            File.WriteAllBytes(dll, []);
            try
            {
                container.Resolve<IMediator>().Send(new DropPathsRequest([dll])).AsTask().GetAwaiter().GetResult();

                var read = bridge.InvokeMethod(IpcNames.Services.Files, IpcNames.Methods.FilesGet, "{}").GetAwaiter().GetResult();
                var bad = bridge.InvokeMethod(
                    IpcNames.Services.Files, IpcNames.Methods.FilesSetIncluded, """{"id":"nope","included":false}""").GetAwaiter().GetResult();

                read.ShouldContain("\"success\":true");
                read.ShouldContain(Path.GetFileName(dll));
                read.ShouldContain("\"hasIncludedFiles\":true");
                bad.ShouldContain("\"success\":false");
                bad.ShouldContain("files.notFound");
                container.Resolve<InputSession>().GetSnapshot().Files.Count.ShouldBe(1);
            }
            finally
            {
                File.Delete(dll);
            }

            return true;
        });
    }

    [Fact]
    public void EveryFilesAndOutputMethod_IsRegistered_AndFilesChangedUsesTheAppEventName()
    {
        using var container = Build();
        var registry = container.Resolve<ApiRegistry>();
        ApiRegistrations.RegisterAll(registry);

        foreach (var method in new[]
        {
            IpcNames.Methods.FilesGet, IpcNames.Methods.FilesPick, IpcNames.Methods.FilesRemove, IpcNames.Methods.FilesClear,
            IpcNames.Methods.FilesSetIncluded, IpcNames.Methods.FilesReveal, IpcNames.Methods.FilesOpenOutput
        })
            registry.GetRequestType(IpcNames.Services.Files, method).ShouldNotBeNull(method);

        foreach (var method in new[] { IpcNames.Methods.OutputSet, IpcNames.Methods.OutputPickFolder, IpcNames.Methods.OutputSetSymbolMap })
            registry.GetRequestType(IpcNames.Services.Output, method).ShouldNotBeNull(method);

        // The native drop is not a page method.
        registry.GetRequestType(IpcNames.Services.Files, "dropPaths").ShouldBeNull();
        IpcNames.Events.FilesChanged.ShouldBe(Obfy.App.AppEvents.FilesChanged);
        IpcNames.Events.FilesChanged.ShouldBe("files.changed");
    }
}
