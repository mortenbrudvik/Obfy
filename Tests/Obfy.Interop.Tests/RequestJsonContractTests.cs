using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Obfy.App;
using Obfy.App.Features.Files;
using Obfy.App.Features.Output;
using Obfy.App.Features.Preview;
using Obfy.App.Features.Results;
using Obfy.App.Features.Settings;
using Obfy.App.Models;
using Obfy.Common;
using Obfy.Interop;
using Obfy.WebView;
using Shouldly;
using AppInfo = Obfy.App.Features.App.AppInfo;

namespace Obfy.Interop.Tests;

/// <summary>
/// Pins the wire shape the page's hand-written TypeScript types rely on: camelCase names, enums by name, the exact set
/// of fields per snapshot. A field renamed or added on the C# side fails here until the page's types are updated too.
/// </summary>
public class RequestJsonContractTests
{
    private sealed class CapturingWebView : IWebViewInterop
    {
        public List<string> Posted { get; } = [];
        public void PostWebMessageAsJson(string json) => Posted.Add(json);
    }

    private static JsonElement Json(object value) => JsonDocument.Parse(JsonUtils.Serialize(value)).RootElement;

    private static string[] Keys(JsonElement element) => element.EnumerateObject().Select(p => p.Name).ToArray();

    private static void ShouldHaveKeys(JsonElement element, params string[] expected) =>
        Keys(element).ShouldBe(expected, ignoreOrder: true);

    private static void ShouldBeString(JsonElement element, string expected)
    {
        element.ValueKind.ShouldBe(JsonValueKind.String);
        element.GetString().ShouldBe(expected);
    }

    // ---- Requests the page sends -------------------------------------------------------------------------------

    [Theory]
    [InlineData(typeof(Obfy.App.Features.App.AppInfoRequest))]
    [InlineData(typeof(Obfy.App.Features.App.ReadyRequest))]
    [InlineData(typeof(GetSettingsRequest))]
    [InlineData(typeof(OpenConfigRequest))]
    [InlineData(typeof(PickSigningKeyRequest))]
    [InlineData(typeof(GetFilesRequest))]
    [InlineData(typeof(PickFilesRequest))]
    [InlineData(typeof(ClearFilesRequest))]
    [InlineData(typeof(OpenOutputRequest))]
    [InlineData(typeof(PickOutputFolderRequest))]
    [InlineData(typeof(PickSymbolMapRequest))]
    [InlineData(typeof(Obfy.App.Features.Log.GetLogRequest))]
    [InlineData(typeof(Obfy.App.Features.Log.ClearLogRequest))]
    [InlineData(typeof(GetResultsSummaryRequest))]
    [InlineData(typeof(ExportMapRequest))]
    [InlineData(typeof(ExportReportRequest))]
    [InlineData(typeof(Obfy.App.Features.Run.GetRunRequest))]
    [InlineData(typeof(Obfy.App.Features.Run.StartRunRequest))]
    [InlineData(typeof(Obfy.App.Features.Run.CancelRunRequest))]
    public void ParameterlessRequests_AcceptAnEmptyObject(Type request) =>
        JsonUtils.Deserialize("{}", request).ShouldBeOfType(request);

    [Fact]
    public void UpdateSettings_ReadsADottedPatchKeyAsIs()
    {
        var request = (UpdateSettingsRequest)JsonUtils.Deserialize(
            """{"patch":{"controlFlow.enabled":true,"watermark.id":"acme"}}""", typeof(UpdateSettingsRequest))!;

        request.Patch.Keys.ShouldBe(["controlFlow.enabled", "watermark.id"], ignoreOrder: true);
        request.Patch["controlFlow.enabled"].GetBoolean().ShouldBeTrue();
        request.Patch["watermark.id"].GetString().ShouldBe("acme");
    }

    [Fact]
    public void OpenGuide_ReadsTheTopic_AndLeavesItNullWhenMissing()
    {
        var type = typeof(Obfy.App.Features.App.OpenGuideRequest);

        ((Obfy.App.Features.App.OpenGuideRequest)JsonUtils.Deserialize("""{"topic":"techniques"}""", type)!).Topic.ShouldBe("techniques");
        ((Obfy.App.Features.App.OpenGuideRequest)JsonUtils.Deserialize("""{"topic":null}""", type)!).Topic.ShouldBeNull();
        ((Obfy.App.Features.App.OpenGuideRequest)JsonUtils.Deserialize("{}", type)!).Topic.ShouldBeNull();
        ShouldHaveKeys(Json(new Obfy.App.Features.App.OpenGuideRequest("techniques")), "topic");
    }

    [Fact]
    public void ApplyLevel_ReadsLevelAsAString() =>
        ((ApplyLevelRequest)JsonUtils.Deserialize("""{"level":"Aggressive"}""", typeof(ApplyLevelRequest))!).Level.ShouldBe("Aggressive");

    [Fact]
    public void SaveConfig_ReadsSaveAs()
    {
        ((SaveConfigRequest)JsonUtils.Deserialize("""{"saveAs":true}""", typeof(SaveConfigRequest))!).SaveAs.ShouldBeTrue();
        ((SaveConfigRequest)JsonUtils.Deserialize("{}", typeof(SaveConfigRequest))!).SaveAs.ShouldBeFalse();
    }

    [Fact]
    public void FileRowRequests_ReadTheRowId()
    {
        ((RemoveFileRequest)JsonUtils.Deserialize("""{"id":"abc"}""", typeof(RemoveFileRequest))!).Id.ShouldBe("abc");
        ((RevealFileRequest)JsonUtils.Deserialize("""{"id":"abc"}""", typeof(RevealFileRequest))!).Id.ShouldBe("abc");

        var included = (SetIncludedRequest)JsonUtils.Deserialize("""{"id":"abc","included":false}""", typeof(SetIncludedRequest))!;
        included.Id.ShouldBe("abc");
        included.Included.ShouldBeFalse();
    }

    [Fact]
    public void OutputRequests_ReadFolderEnabledAndPath()
    {
        ((SetOutputRequest)JsonUtils.Deserialize("""{"folder":"C:\\out"}""", typeof(SetOutputRequest))!).Folder.ShouldBe("C:\\out");

        var map = (SetSymbolMapRequest)JsonUtils.Deserialize("""{"enabled":true,"path":"map.json"}""", typeof(SetSymbolMapRequest))!;
        map.Enabled.ShouldBe(true);
        map.Path.ShouldBe("map.json");
        var neither = (SetSymbolMapRequest)JsonUtils.Deserialize("{}", typeof(SetSymbolMapRequest))!;
        neither.Enabled.ShouldBeNull();
        neither.Path.ShouldBeNull();
    }

    [Fact]
    public void QuerySymbols_ReadsTextParentIdOffsetAndLimit()
    {
        var query = (QuerySymbolsRequest)JsonUtils.Deserialize(
            """{"text":"Foo","parentId":"n1","offset":50,"limit":25}""", typeof(QuerySymbolsRequest))!;

        query.Text.ShouldBe("Foo");
        query.ParentId.ShouldBe("n1");
        query.Offset.ShouldBe(50);
        query.Limit.ShouldBe(25);
        var defaults = (QuerySymbolsRequest)JsonUtils.Deserialize("{}", typeof(QuerySymbolsRequest))!;
        defaults.Offset.ShouldBe(0);
        defaults.Limit.ShouldBe(200);
    }

    [Fact]
    public void GetPreview_ReadsPath() =>
        ((GetPreviewRequest)JsonUtils.Deserialize("""{"path":"C:\\out\\a.dll"}""", typeof(GetPreviewRequest))!).Path.ShouldBe("C:\\out\\a.dll");

    [Fact]
    public void RequestsSerializeWithCamelCaseNames()
    {
        ShouldHaveKeys(Json(new SetIncludedRequest("a", true)), "id", "included");
        ShouldHaveKeys(Json(new QuerySymbolsRequest("t", "p", 1, 2)), "text", "parentId", "offset", "limit");
        ShouldHaveKeys(Json(new SetSymbolMapRequest(true, "p")), "enabled", "path");
        ShouldHaveKeys(Json(new SaveConfigRequest(true)), "saveAs");
        ShouldHaveKeys(Json(new UpdateSettingsRequest(new Dictionary<string, JsonElement>())), "patch");
    }

    // ---- Snapshots the page receives ---------------------------------------------------------------------------

    [Fact]
    public void AppInfo_HasVersion() => ShouldHaveKeys(Json(new AppInfo("1.2.3")), "version");

    [Fact]
    public void SettingsSnapshot_HasTheFieldsThePageReads()
    {
        var snapshot = new SettingsSnapshot(
            3, "Custom", "summary",
            [new TechniqueState("stringEncryption", true, false, "why", true, new Dictionary<string, object?> { ["algorithm"] = "Aes" })],
            [new ValidationError("watermark.id", "required")],
            "C:\\obfy.json", true, "NativeAot",
            new NameLists(["N"], ["T"], ["M"]), new NameLists([], [], []),
            new SettingsChoices(["Minimal"], ["Xor"], ["Flat"], ["Short"], ["Default"], ["win-x64"]));

        var json = Json(snapshot);

        ShouldHaveKeys(json, "revision", "level", "levelSummary", "techniques", "errors", "configPath", "dirty",
            "runtimeProfile", "exclusions", "inclusions", "choices");
        ShouldHaveKeys(json.GetProperty("techniques")[0], "id", "enabled", "available", "unavailableReason", "differsFromLevel", "options");
        json.GetProperty("techniques")[0].GetProperty("options").GetProperty("algorithm").GetString().ShouldBe("Aes");
        ShouldHaveKeys(json.GetProperty("errors")[0], "field", "message");
        ShouldHaveKeys(json.GetProperty("exclusions"), "namespaces", "types", "methods");
        ShouldHaveKeys(json.GetProperty("inclusions"), "namespaces", "types", "methods");
        ShouldHaveKeys(json.GetProperty("choices"), "levels", "algorithms", "controlFlowModes", "namingModes", "runtimeProfiles", "packingRids");
        ShouldBeString(json.GetProperty("level"), "Custom");
        ShouldBeString(json.GetProperty("runtimeProfile"), "NativeAot");
    }

    [Fact]
    public void InputSnapshot_HasTheFieldsThePageReads_AndEnumsByName()
    {
        var row = new FileRow("id1", "a.dll", "C:\\a.dll", FileOrigin.Solution, FileKind.Project, 10, "10 B", false,
            FileStatus.Skipped, "no output", "C:\\o\\a.dll", null);
        var snapshot = new InputSnapshot(
            4, [row], true, "C:\\o", "hint", "merge hint", "gone", true, "map.json", "C:\\o\\map.json",
            [new InputNotice(NoticeLevel.Warning, "Title", "Message")]);

        var json = Json(snapshot);

        ShouldHaveKeys(json, "revision", "files", "hasIncludedFiles", "outputFolder", "outputHint", "mergeOutputHint",
            "outputWarning", "writeSymbolMap", "symbolMapPath", "resolvedSymbolMapPath", "notices");
        var file = json.GetProperty("files")[0];
        ShouldHaveKeys(file, "id", "name", "path", "origin", "kind", "size", "sizeText", "included", "status", "skipReason", "outputPath", "error");
        ShouldBeString(file.GetProperty("origin"), "Solution");
        ShouldBeString(file.GetProperty("kind"), "Project");
        ShouldBeString(file.GetProperty("status"), "Skipped");
        var notice = json.GetProperty("notices")[0];
        ShouldHaveKeys(notice, "level", "title", "message");
        ShouldBeString(notice.GetProperty("level"), "Warning");
    }

    [Fact]
    public void LogSnapshotAndLogAppended_HaveTheFieldsThePageReads_AndLevelByName()
    {
        var lines = new[] { new LogLine(7, "12:00:00.123", Obfy.App.Models.LogLevel.Success, "done") };

        var snapshot = Json(new LogSnapshot(9, lines));
        var appended = Json(new LogAppended(9, lines));

        ShouldHaveKeys(snapshot, "revision", "lines");
        ShouldHaveKeys(appended, "revision", "lines");
        foreach (var json in new[] { snapshot, appended })
        {
            var line = json.GetProperty("lines")[0];
            ShouldHaveKeys(line, "id", "timestamp", "level", "message");
            ShouldBeString(line.GetProperty("level"), "Success");
            line.GetProperty("id").ValueKind.ShouldBe(JsonValueKind.Number);
        }
    }

    [Fact]
    public void ResultsSnapshot_HasTheFieldsThePageReads_AndStateByName()
    {
        var snapshot = new ResultsSnapshot(
            5, ResultsState.Finished, [new StatisticGroup("Renamed", [new StatisticChip("Types", 3)])], 1200, 42, true, true,
            ["C:\\o\\a.dll"], "C:\\o\\a.dll", null);

        var json = Json(snapshot);

        ShouldHaveKeys(json, "revision", "state", "groups", "elapsedMs", "symbolCount", "hasReport", "hasPreview",
            "previewPaths", "selectedPreviewPath", "previewError");
        ShouldBeString(json.GetProperty("state"), "Finished");
        var group = json.GetProperty("groups")[0];
        ShouldHaveKeys(group, "name", "chips");
        ShouldHaveKeys(group.GetProperty("chips")[0], "label", "value");
    }

    [Fact]
    public void SymbolsPage_HasTheFieldsThePageReads_AndKindByName()
    {
        var page = new SymbolsPage(
            5, null, 1, 0, [new SymbolNode("n1", "Foo", "a", SymbolKind.Method, false, 0, true)], 10, false, false);

        var json = Json(page);

        ShouldHaveKeys(json, "revision", "parentId", "total", "offset", "items", "totalSymbols", "hasNoSymbols", "hasNoSearchMatches");
        var node = json.GetProperty("items")[0];
        ShouldHaveKeys(node, "id", "originalName", "obfuscatedName", "kind", "isContainer", "childCount", "expanded");
        ShouldBeString(node.GetProperty("kind"), "Method");
    }

    [Fact]
    public void PreviewAndExportResults_HaveTheFieldsThePageReads()
    {
        // hasPreview is computed on the C# side and is part of the wire shape.
        ShouldHaveKeys(Json(new PreviewResult("C:\\a.dll", "class A {}", null)), "path", "text", "error", "hasPreview");
        Json(new PreviewResult("C:\\a.dll", "class A {}", null)).GetProperty("hasPreview").GetBoolean().ShouldBeTrue();
        ShouldHaveKeys(Json(new ExportResult(true, "C:\\m.json")), "exported", "path");
    }

    [Fact]
    public void RunSnapshot_HasTheFieldsThePageReads_AndEnumsByName()
    {
        var snapshot = new RunSnapshot(
            6, false, "nothing included", RunMode.ClosedSet, "explained", "hint", 42.5, true, "a.dll", 1, 3, 0, RunStatus.Running, "working");

        var json = Json(snapshot);

        ShouldHaveKeys(json, "revision", "canStart", "blockedReason", "mode", "modeExplanation", "outputHint", "progress",
            "indeterminate", "currentFile", "completed", "total", "failed", "status", "message");
        ShouldBeString(json.GetProperty("mode"), "ClosedSet");
        ShouldBeString(json.GetProperty("status"), "Running");
        json.GetProperty("progress").GetDouble().ShouldBe(42.5);
    }

    [Theory]
    [InlineData(RunMode.None, "None")]
    [InlineData(RunMode.Single, "Single")]
    [InlineData(RunMode.Sources, "Sources")]
    [InlineData(RunMode.Merge, "Merge")]
    [InlineData(RunMode.ClosedSet, "ClosedSet")]
    public void RunMode_SerializesByName(RunMode mode, string name) =>
        ShouldBeString(Json(new { mode }).GetProperty("mode"), name);

    [Theory]
    [InlineData(RunStatus.Idle, "Idle")]
    [InlineData(RunStatus.Running, "Running")]
    [InlineData(RunStatus.Done, "Done")]
    [InlineData(RunStatus.Cancelled, "Cancelled")]
    [InlineData(RunStatus.Faulted, "Faulted")]
    public void RunStatus_SerializesByName(RunStatus status, string name) =>
        ShouldBeString(Json(new { status }).GetProperty("status"), name);

    [Fact]
    public void ErrorEnvelope_HasCodeAndMessage()
    {
        var json = Json(new { success = false, error = new ApiError("files.notFound", "gone") });

        ShouldHaveKeys(json, "success", "error");
        ShouldHaveKeys(json.GetProperty("error"), "code", "message");
    }

    // ---- Events the page receives ------------------------------------------------------------------------------

    private static JsonElement Pushed(string eventName, object? data)
    {
        var webView = new CapturingWebView();
        new ApiEventAggregator(webView, new IpcLogger(NullLogger<IpcLogger>.Instance)).Publish(eventName, data);
        return JsonDocument.Parse(webView.Posted.ShouldHaveSingleItem()).RootElement;
    }

    [Fact]
    public void EventEnvelope_IsEventNameAndData()
    {
        var envelope = Pushed(AppEvents.RunChanged, new { Value = 1 });

        ShouldHaveKeys(envelope, "eventName", "data");
        ShouldBeString(envelope.GetProperty("eventName"), "run.changed");
    }

    [Fact]
    public void EveryAppEvent_CarriesItsSnapshotAsData()
    {
        var run = new RunSnapshot(1, true, null, RunMode.Single, "m", "h", 0, true, null, 0, 1, 0, RunStatus.Idle, "ok");
        var settings = new SettingsSnapshot(
            1, "Standard", "s", [], [], null, false, "Default", new NameLists([], [], []), new NameLists([], [], []),
            new SettingsChoices([], [], [], [], [], []));
        var input = new InputSnapshot(1, [], false, "", "", "", null, false, "", null, []);
        var lines = new[] { new LogLine(1, "00:00:00.000", Obfy.App.Models.LogLevel.Info, "x") };
        var results = new ResultsSnapshot(1, ResultsState.NotRun, [], 0, 0, false, false, [], null, null);

        var cases = new (string Event, object Payload, string[] DataKeys)[]
        {
            (AppEvents.SettingsChanged, settings, ["revision", "level", "levelSummary", "techniques", "errors", "configPath", "dirty",
                "runtimeProfile", "exclusions", "inclusions", "choices"]),
            (AppEvents.FilesChanged, input, ["revision", "files", "hasIncludedFiles", "outputFolder", "outputHint", "mergeOutputHint",
                "outputWarning", "writeSymbolMap", "symbolMapPath", "resolvedSymbolMapPath", "notices"]),
            (AppEvents.LogAppended, new LogAppended(1, lines), ["revision", "lines"]),
            (AppEvents.LogCleared, new LogSnapshot(2, []), ["revision", "lines"]),
            (AppEvents.ResultsChanged, results, ["revision", "state", "groups", "elapsedMs", "symbolCount", "hasReport", "hasPreview",
                "previewPaths", "selectedPreviewPath", "previewError"]),
            (AppEvents.RunChanged, run, ["revision", "canStart", "blockedReason", "mode", "modeExplanation", "outputHint", "progress",
                "indeterminate", "currentFile", "completed", "total", "failed", "status", "message"]),
            (AppEvents.RunCompleted, run, ["revision", "canStart", "blockedReason", "mode", "modeExplanation", "outputHint", "progress",
                "indeterminate", "currentFile", "completed", "total", "failed", "status", "message"]),
        };

        // Every event the application layer pushes is covered, so a new AppEvents constant fails here until it is pinned.
        cases.Select(c => c.Event).ShouldBe(
            typeof(AppEvents).GetFields().Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!), ignoreOrder: true);

        foreach (var (name, payload, dataKeys) in cases)
        {
            var envelope = Pushed(name, payload);
            ShouldBeString(envelope.GetProperty("eventName"), name);
            ShouldHaveKeys(envelope.GetProperty("data"), dataKeys);
        }
    }

    [Fact]
    public void AppRevealed_CarriesTheReadyFlag()
    {
        var envelope = Pushed(IpcNames.Events.AppRevealed, new { ready = true });

        ShouldBeString(envelope.GetProperty("eventName"), "app.revealed");
        ShouldHaveKeys(envelope.GetProperty("data"), "ready");
    }
}
