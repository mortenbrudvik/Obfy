using System.Reflection;
using Mediator;
using Obfy.App;
using Obfy.App.Features.App;
using Obfy.App.Features.Files;
using Obfy.App.Features.Log;
using Obfy.App.Features.Output;
using Obfy.App.Features.Preview;
using Obfy.App.Features.Results;
using Obfy.App.Features.Run;
using Obfy.App.Features.Settings;
using Obfy.Interop;
using Shouldly;

namespace Obfy.Interop.Tests;

/// <summary>
/// Pins the whole bridge surface. The page mirrors these names by hand, so a rename or a dropped registration must fail
/// here rather than at runtime in the page.
/// </summary>
public class ApiRegistrationsTests
{
    /// <summary>
    /// Requests under Obfy.App that are deliberately not callable from the page. Each is sent by the host only; adding a
    /// request type to this list is a conscious decision that the page must never be able to send it.
    /// </summary>
    private static readonly Dictionary<Type, string> NotBridged = new()
    {
        [typeof(DropPathsRequest)] = "Sent by the native window drop; the page cannot name paths.",
        [typeof(LoadConfigRequest)] = "Sent by the host for a dropped .json or a startup argument; the page cannot name paths.",
    };

    /// <summary>Every <c>(service, method, request)</c> the page may call. Literal strings on purpose: they are the wire names.</summary>
    public static TheoryData<string, string, Type> Surface => new()
    {
        { "app", "info", typeof(AppInfoRequest) },
        { "app", "ready", typeof(ReadyRequest) },

        { "settings", "get", typeof(GetSettingsRequest) },
        { "settings", "update", typeof(UpdateSettingsRequest) },
        { "settings", "applyLevel", typeof(ApplyLevelRequest) },
        { "settings", "openConfig", typeof(OpenConfigRequest) },
        { "settings", "saveConfig", typeof(SaveConfigRequest) },
        { "settings", "pickSigningKey", typeof(PickSigningKeyRequest) },

        { "files", "get", typeof(GetFilesRequest) },
        { "files", "pick", typeof(PickFilesRequest) },
        { "files", "remove", typeof(RemoveFileRequest) },
        { "files", "clear", typeof(ClearFilesRequest) },
        { "files", "setIncluded", typeof(SetIncludedRequest) },
        { "files", "reveal", typeof(RevealFileRequest) },
        { "files", "openOutput", typeof(OpenOutputRequest) },

        { "output", "set", typeof(SetOutputRequest) },
        { "output", "pickFolder", typeof(PickOutputFolderRequest) },
        { "output", "setSymbolMap", typeof(SetSymbolMapRequest) },

        { "log", "get", typeof(GetLogRequest) },
        { "log", "clear", typeof(ClearLogRequest) },

        { "results", "summary", typeof(GetResultsSummaryRequest) },
        { "results", "symbols", typeof(QuerySymbolsRequest) },
        { "results", "exportMap", typeof(ExportMapRequest) },
        { "results", "exportReport", typeof(ExportReportRequest) },

        { "preview", "get", typeof(GetPreviewRequest) },

        { "run", "get", typeof(GetRunRequest) },
        { "run", "start", typeof(StartRunRequest) },
        { "run", "cancel", typeof(CancelRunRequest) },
    };

    private static ApiRegistry Registered()
    {
        var registry = new ApiRegistry();
        ApiRegistrations.RegisterAll(registry);
        return registry;
    }

    private static List<(string Service, string Method, Type Request)> SurfaceRows() =>
        Surface.Select(row => ((string)row[0], (string)row[1], (Type)row[2])).ToList();

    [Theory]
    [MemberData(nameof(Surface))]
    public void RegisterAll_MapsEachCallToItsRequest(string service, string method, Type expected) =>
        Registered().GetRequestType(service, method).ShouldBe(expected);

    [Fact]
    public void RegisterAll_RegistersNothingBeyondTheSurface()
    {
        var registered = Registered().GetAll();

        registered.Keys.OrderBy(k => k, StringComparer.Ordinal)
            .ShouldBe(SurfaceRows().Select(r => $"{r.Service}.{r.Method}").OrderBy(k => k, StringComparer.Ordinal));
    }

    [Fact]
    public void EveryRequestInTheAppAssembly_IsRegistered_OrExplicitlyNotBridged()
    {
        var registeredTypes = Registered().GetAll().Values.ToHashSet();
        var requests = typeof(AppInfoRequest).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IBaseRequest).IsAssignableFrom(t))
            .ToList();

        requests.ShouldNotBeEmpty();
        var unreachable = requests.Where(t => !registeredTypes.Contains(t) && !NotBridged.ContainsKey(t)).Select(t => t.Name).ToList();
        unreachable.ShouldBeEmpty("These requests have a handler but no registration; register them or add them to NotBridged.");
    }

    [Fact]
    public void EveryRegistration_PointsToARealRequestInTheAppAssembly()
    {
        var appTypes = typeof(AppInfoRequest).Assembly.GetTypes().ToHashSet();

        foreach (var (name, type) in Registered().GetAll())
        {
            appTypes.ShouldContain(type, name);
            typeof(IBaseRequest).IsAssignableFrom(type).ShouldBeTrue(name);
            NotBridged.ShouldNotContainKey(type, $"{name} is registered but listed as not bridged.");
        }
    }

    [Fact]
    public void EveryRequest_HasExactlyOneRegistration()
    {
        var types = Registered().GetAll().Values.ToList();

        types.Distinct().Count().ShouldBe(types.Count);
    }

    [Fact]
    public void IpcNamesServices_CoverEveryRegisteredServiceAndNothingElse()
    {
        var services = ConstantsOf(typeof(IpcNames.Services)).Select(c => c.Value).ToHashSet();

        services.ShouldBe(SurfaceRows().Select(r => r.Service).ToHashSet(), ignoreOrder: true);
    }

    [Fact]
    public void IpcNamesMethods_CoverEveryRegisteredMethod_OneConstantEach()
    {
        var methods = ConstantsOf(typeof(IpcNames.Methods)).ToList();

        // One constant per registration: a new method needs both, and a stale constant fails here.
        methods.Count.ShouldBe(Registered().GetAll().Count);
        var values = methods.Select(m => m.Value).ToHashSet();
        foreach (var row in SurfaceRows())
            values.ShouldContain(row.Method, $"{row.Service}.{row.Method}");
    }

    [Fact]
    public void IpcNamesMethods_UseCamelCaseValuesWithoutDots()
    {
        foreach (var (name, value) in ConstantsOf(typeof(IpcNames.Methods)))
        {
            char.IsLower(value[0]).ShouldBeTrue($"{name} = {value}");
            value.ShouldNotContain(".");
        }
    }

    [Fact]
    public void IpcNamesEvents_EqualTheAppEventsConstants_ByNameAndValue()
    {
        var appEvents = ConstantsOf(typeof(AppEvents)).ToDictionary(c => c.Name, c => c.Value);
        var ipcEvents = ConstantsOf(typeof(IpcNames.Events)).ToDictionary(c => c.Name, c => c.Value);

        appEvents.ShouldNotBeEmpty();
        foreach (var (name, value) in appEvents)
        {
            ipcEvents.ShouldContainKey(name);
            ipcEvents[name].ShouldBe(value, name);
        }
    }

    [Fact]
    public void IpcNamesEvents_HoldOnlyTheAppEventsAndTheShellsOwn()
    {
        var appEventNames = ConstantsOf(typeof(AppEvents)).Select(c => c.Name).ToHashSet();
        var shellOwned = ConstantsOf(typeof(IpcNames.Events)).Where(c => !appEventNames.Contains(c.Name)).ToList();

        // app.revealed is pushed by the shell (ShellViewModel), not by the application layer, so AppEvents has no copy of it.
        shellOwned.ShouldHaveSingleItem().Value.ShouldBe("app.revealed");
    }

    [Fact]
    public void EventNames_AreUnique()
    {
        var values = ConstantsOf(typeof(IpcNames.Events)).Select(c => c.Value).ToList();

        values.Distinct().Count().ShouldBe(values.Count);
    }

    private static IEnumerable<(string Name, string Value)> ConstantsOf(Type type) =>
        type.GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f is { IsLiteral: true, IsInitOnly: false } && f.FieldType == typeof(string))
            .Select(f => (f.Name, (string)f.GetRawConstantValue()!));
}
