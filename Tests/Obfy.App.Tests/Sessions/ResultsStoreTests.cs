using Obfy.App.Features.Preview;
using Obfy.App.Features.Results;
using Obfy.App.Models;
using Obfy.App.Services;
using Obfy.App.Sessions;
using Obfy.App.Tests.Common;
using Obfy.Core.Models;
using Obfy.Core.Pipeline;
using Obfy.Core.Services.Reporting;
using Shouldly;

namespace Obfy.App.Tests.Sessions;

public class ResultsStoreTests : IDisposable
{
    private readonly FakeEvents _events = new();
    private readonly FakeDialogs _dialogs = new();
    private readonly FakeReports _reports = new();
    private readonly ResultsStore _store;
    private readonly string _tempDirectory;

    public ResultsStoreTests()
    {
        _store = new ResultsStore(_events);
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"ResultsStoreTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDirectory))
                Directory.Delete(_tempDirectory, recursive: true);
        }
        catch (IOException ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }

    private Task LoadAsync(Dictionary<string, string> map, ObfuscationStatistics? stats = null, TimeSpan? elapsed = null) =>
        _store.LoadResultsAsync(map, stats ?? new ObfuscationStatistics(), elapsed ?? TimeSpan.Zero);

    private SymbolsPage Roots(string? text = null) => _store.Query(text, null, 0, 100);

    // ---- ported from ResultsViewModelTests ----

    [Fact]
    public async Task LoadResults_GroupsMembersUnderType()
    {
        await LoadAsync(
            new Dictionary<string, string>
            {
                ["Type:MyType"] = "a",
                ["Method:MyType.Foo(System.Void ())"] = "b",
                ["Field:MyType.Bar"] = "c"
            },
            new ObfuscationStatistics { TypesRenamed = 1 },
            TimeSpan.FromSeconds(1));

        var roots = Roots();
        roots.Items.Count.ShouldBe(1);
        roots.Items[0].OriginalName.ShouldBe("MyType");
        roots.Items[0].ObfuscatedName.ShouldBe("a");
        roots.Items[0].ChildCount.ShouldBe(2);
        roots.HasNoSymbols.ShouldBeFalse();
        roots.HasNoSearchMatches.ShouldBeFalse();
        _store.GetSnapshot().HasReport.ShouldBeFalse();
        _store.GetSnapshot().ElapsedMs.ShouldBe(1000);
    }

    [Fact]
    public async Task Search_KeepsMatchingHierarchy()
    {
        await LoadAsync(new Dictionary<string, string>
        {
            ["Type:TypeA"] = "x",
            ["Method:TypeA.Foo(System.Void ())"] = "y",
            ["Method:TypeA.Bar(System.Void ())"] = "z",
            ["Type:TypeB"] = "w"
        });

        var roots = Roots("Foo");

        roots.Items.Count.ShouldBe(1);
        roots.Items[0].OriginalName.ShouldBe("TypeA");
        roots.Items[0].ChildCount.ShouldBe(1);
        roots.Items[0].Expanded.ShouldBeTrue();
        var children = _store.Query("Foo", roots.Items[0].Id, 0, 100);
        children.Items.Count.ShouldBe(1);
        children.Items[0].OriginalName.ShouldBe("Foo(System.Void ())");
        roots.HasNoSearchMatches.ShouldBeFalse();
    }

    [Fact]
    public async Task Search_NoMatches_SetsEmptyState()
    {
        await LoadAsync(new Dictionary<string, string> { ["TypeA"] = "x" });

        var roots = Roots("zzzz");

        roots.Items.Count.ShouldBe(0);
        roots.HasNoSearchMatches.ShouldBeTrue();
        roots.HasNoSymbols.ShouldBeFalse();
    }

    [Fact]
    public async Task Clear_ResetsReportAndTree()
    {
        await LoadAsync(new Dictionary<string, string> { ["TypeA"] = "x" }, elapsed: TimeSpan.FromSeconds(2));
        _store.SetReport(new ObfuscationReport());

        _store.Clear();

        var roots = Roots();
        roots.Items.Count.ShouldBe(0);
        roots.HasNoSymbols.ShouldBeTrue();
        var snapshot = _store.GetSnapshot();
        snapshot.HasReport.ShouldBeFalse();
        snapshot.ElapsedMs.ShouldBe(0);
        snapshot.Groups.ShouldBeEmpty();
        _store.GetReport().ShouldBeNull();
    }

    [Fact]
    public async Task ExportReport_WhenDialogCancelled_DoesNotGenerate()
    {
        _store.SetReport(new ObfuscationReport());
        _dialogs.SavePath = null;

        var result = await new ExportReportRequestHandler(_store, _reports, _dialogs)
            .Handle(new ExportReportRequest(), CancellationToken.None);

        result.Exported.ShouldBeFalse();
        _reports.Generated.ShouldBeEmpty();
    }

    [Fact]
    public async Task ExportReport_WhenWriteFails_ThrowsApiErrorWithMessage()
    {
        _store.SetReport(new ObfuscationReport());
        _dialogs.SavePath = Path.Combine(_tempDirectory, "report.html");
        _reports.Failure = new IOException("disk full");

        var error = await Should.ThrowAsync<ObfyApiException>(async () =>
            await new ExportReportRequestHandler(_store, _reports, _dialogs)
                .Handle(new ExportReportRequest(), CancellationToken.None));

        error.Message.ShouldContain("disk full");
    }

    [Theory]
    [InlineData("report.html", ReportFormat.Html)]
    [InlineData("report.JSON", ReportFormat.Json)]
    public async Task ExportReport_PicksFormatFromExtension(string name, ReportFormat expected)
    {
        _store.SetReport(new ObfuscationReport());
        var path = Path.Combine(_tempDirectory, name);
        _dialogs.SavePath = path;

        var result = await new ExportReportRequestHandler(_store, _reports, _dialogs)
            .Handle(new ExportReportRequest(), CancellationToken.None);

        result.Exported.ShouldBeTrue();
        _reports.Generated.ShouldHaveSingleItem().ShouldBe((path, expected));
    }

    [Fact]
    public async Task ExportReport_WithNoReport_Throws()
    {
        await Should.ThrowAsync<ObfyApiException>(async () =>
            await new ExportReportRequestHandler(_store, _reports, _dialogs)
                .Handle(new ExportReportRequest(), CancellationToken.None));
        _dialogs.SavePicks.ShouldBe(0);
    }

    [Fact]
    public async Task ExportSymbolMap_WritesIndentedJson()
    {
        var path = Path.Combine(_tempDirectory, "map.json");
        _dialogs.SavePath = path;
        await LoadAsync(new Dictionary<string, string> { ["Foo"] = "a" });

        var result = await new ExportMapRequestHandler(_store, _dialogs)
            .Handle(new ExportMapRequest(), CancellationToken.None);

        result.Exported.ShouldBeTrue();
        result.Path.ShouldBe(path);
        var json = File.ReadAllText(path);
        json.ShouldContain("Foo");
        json.ShouldContain(Environment.NewLine);
    }

    [Fact]
    public async Task ExportSymbolMap_WhenDialogCancelled_WritesNothing()
    {
        await LoadAsync(new Dictionary<string, string> { ["Foo"] = "a" });

        var result = await new ExportMapRequestHandler(_store, _dialogs)
            .Handle(new ExportMapRequest(), CancellationToken.None);

        result.Exported.ShouldBeFalse();
    }

    [Fact]
    public async Task LoadPreview_MissingPath_SetsError()
    {
        var result = await _store.LoadPreviewAsync(null);

        result.Text.ShouldBeEmpty();
        result.Error.ShouldNotBeNull();
        result.HasPreview.ShouldBeFalse();
        _store.GetSnapshot().HasPreview.ShouldBeFalse();
        _store.GetSnapshot().PreviewError.ShouldNotBeNull();
    }

    [Fact]
    public async Task LoadPreview_MissingFile_SetsError()
    {
        var result = await _store.LoadPreviewAsync(Path.Combine(_tempDirectory, "missing.dll"));

        result.Error.ShouldNotBeNull();
        result.Error!.ShouldContain("not found");
        result.HasPreview.ShouldBeFalse();
    }

    [Fact]
    public async Task LoadPreview_Directory_SetsError()
    {
        var result = await _store.LoadPreviewAsync(_tempDirectory);

        result.Error.ShouldNotBeNull();
        result.Error!.ShouldContain("assemblies");
        result.HasPreview.ShouldBeFalse();
    }

    [Fact]
    public async Task LoadPreview_NonAssembly_SetsErrorWithoutThrowing()
    {
        var path = Path.Combine(_tempDirectory, "notpe.txt");
        File.WriteAllText(path, "not an assembly");

        var result = await _store.LoadPreviewAsync(path);

        result.Error.ShouldNotBeNull();
        result.Error.ShouldStartWith("Preview failed:");
        result.Text.ShouldBeEmpty();
    }

    [Fact]
    public async Task LoadPreview_ValidAssembly_SetsPreviewText()
    {
        var path = typeof(ResultsStoreTests).Assembly.Location;
        File.Exists(path).ShouldBeTrue();

        var result = await _store.LoadPreviewAsync(path);

        result.Error.ShouldBeNull();
        result.Text.ShouldNotBeEmpty();
        result.HasPreview.ShouldBeTrue();
        _store.GetSnapshot().HasPreview.ShouldBeTrue();
        _store.GetSnapshot().SelectedPreviewPath.ShouldBe(path);
    }

    [Fact]
    public async Task Clear_ResetsPreview()
    {
        await _store.LoadPreviewAsync(typeof(ResultsStoreTests).Assembly.Location);
        _store.GetSnapshot().HasPreview.ShouldBeTrue();

        _store.Clear();

        var snapshot = _store.GetSnapshot();
        snapshot.HasPreview.ShouldBeFalse();
        snapshot.PreviewError.ShouldBeNull();
        snapshot.SelectedPreviewPath.ShouldBeNull();
        _store.GetPreview().Text.ShouldBeEmpty();
    }

    [Fact]
    public async Task Nodes_CarryBothNames_SoThePageCanCopyTheMapping()
    {
        await LoadAsync(new Dictionary<string, string> { ["Type:Foo"] = "a" });

        var node = Roots().Items.ShouldHaveSingleItem();

        $"{node.OriginalName} -> {node.ObfuscatedName}".ShouldBe("Foo -> a");
        node.Kind.ShouldBe(SymbolKind.Type);
    }

    [Fact]
    public async Task MemberNodes_CarryTheirOwnObfuscatedName()
    {
        await LoadAsync(new Dictionary<string, string>
        {
            ["Type:MyType"] = "a",
            ["Field:MyType.Bar"] = "c"
        });

        var type = Roots().Items.ShouldHaveSingleItem();
        var field = _store.Query(null, type.Id, 0, 10).Items.ShouldHaveSingleItem();

        field.OriginalName.ShouldBe("Bar");
        field.ObfuscatedName.ShouldBe("c");
        field.Kind.ShouldBe(SymbolKind.Field);
        field.ChildCount.ShouldBe(0);
    }

    // ---- statistics ----

    [Fact]
    public async Task Statistics_HideZeroCounters_AndEmptyGroups()
    {
        await LoadAsync(
            new Dictionary<string, string>(),
            new ObfuscationStatistics { TypesRenamed = 2, StringsEncrypted = 5 });

        var groups = _store.GetSnapshot().Groups;

        groups.Select(g => g.Name).ShouldBe(["Renamed", "Encrypted", "Total"]);
        groups[0].Chips.ShouldHaveSingleItem().ShouldBe(new StatisticChip("Types", 2));
        groups[1].Chips.ShouldHaveSingleItem().ShouldBe(new StatisticChip("Strings", 5));
        groups.SelectMany(g => g.Chips).ShouldNotContain(c => c.Value == 0);
    }

    [Fact]
    public async Task Statistics_AllZero_HasNoGroups()
    {
        await LoadAsync(new Dictionary<string, string>(), new ObfuscationStatistics());

        _store.GetSnapshot().Groups.ShouldBeEmpty();
    }

    [Fact]
    public async Task Statistics_AllFamilies_AreGrouped()
    {
        await LoadAsync(new Dictionary<string, string>(), new ObfuscationStatistics
        {
            MethodsControlFlowObfuscated = 1,
            MetadataItemsRemoved = 3,
            TypesRenamed = 1
        });

        var groups = _store.GetSnapshot().Groups.Select(g => g.Name).ToList();

        groups.ShouldContain("Protection");
        groups.ShouldContain("Metadata");
        groups.ShouldContain("Renamed");
        groups.Last().ShouldBe("Total");
    }

    // ---- events and revision ----

    [Fact]
    public async Task Changes_PublishResultsChanged_WithRisingRevision()
    {
        await LoadAsync(new Dictionary<string, string> { ["Foo"] = "a" });
        _store.SetReport(new ObfuscationReport());
        _store.SetPreviewPaths(["a.dll"]);

        var snapshots = _events.Published.Where(e => e.Name == AppEvents.ResultsChanged).Select(e => (ResultsSnapshot)e.Data!).ToList();

        snapshots.Count.ShouldBe(3);
        snapshots.Select(s => s.Revision).Distinct().Count().ShouldBe(3);
        snapshots.Select(s => s.Revision).ShouldBe(snapshots.Select(s => s.Revision).OrderBy(r => r));
        _store.GetSnapshot().Revision.ShouldBe(snapshots[^1].Revision);
    }

    [Fact]
    public async Task Changed_IsRaised_AfterEveryChange()
    {
        var raised = 0;
        _store.Changed += () => raised++;

        await LoadAsync(new Dictionary<string, string>());
        _store.Clear();

        raised.ShouldBe(2);
    }

    [Fact]
    public void SetPreviewPaths_DropsBlanksAndDuplicates_AndDoesNotDecompile()
    {
        var decompiles = 0;
        var store = new ResultsStore(_events, new FakeDecompiler(_ => { decompiles++; return "x"; }));

        store.SetPreviewPaths(["a.dll", "", "A.DLL", "b.dll"]);

        store.GetSnapshot().PreviewPaths.ShouldBe(["a.dll", "b.dll"]);
        store.GetSnapshot().SelectedPreviewPath.ShouldBeNull();
        decompiles.ShouldBe(0);
    }

    [Fact]
    public async Task ClearDuringParse_LeavesAConsistentStore()
    {
        var map = new Dictionary<string, string> { ["Type:A"] = "x" };
        var load = _store.LoadResultsAsync(map, new ObfuscationStatistics(), TimeSpan.Zero);
        _store.Clear();
        await load;

        // Whichever side of the race won, the store is consistent: finished with the tree, or not run with nothing.
        var snapshot = _store.GetSnapshot();
        if (snapshot.State == ResultsState.Finished)
            Roots().Items.Count.ShouldBe(1);
        else
            Roots().Items.Count.ShouldBe(0);
    }

    // ---- UI-044: decompile off the calling thread ----

    [Fact]
    public async Task Decompile_RunsOffThread()
    {
        var path = Path.Combine(_tempDirectory, "a.dll");
        File.WriteAllText(path, "x");
        var callerThread = Environment.CurrentManagedThreadId;
        int? decompileThread = null;
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var store = new ResultsStore(_events, new FakeDecompiler(_ =>
        {
            decompileThread = Environment.CurrentManagedThreadId;
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(10));
            return "class A {}";
        }));

        var pending = store.LoadPreviewAsync(path);

        // The call came back while the decompile is still blocked: nothing on this thread waited for it.
        typeof(ResultsStore).GetMethod(nameof(ResultsStore.LoadPreviewAsync))!.ReturnType.ShouldBe(typeof(Task<PreviewResult>));
        entered.Wait(TimeSpan.FromSeconds(10)).ShouldBeTrue();
        pending.IsCompleted.ShouldBeFalse();
        decompileThread.ShouldNotBeNull();
        decompileThread.ShouldNotBe(callerThread);

        release.Set();
        var result = await pending;
        result.Text.ShouldBe("class A {}");
    }

    [Fact]
    public async Task GetPreviewHandler_ReturnsTheDecompile_ThroughTheTask()
    {
        var path = typeof(ResultsStoreTests).Assembly.Location;

        var result = await new GetPreviewRequestHandler(_store).Handle(new GetPreviewRequest(path), CancellationToken.None);

        result.HasPreview.ShouldBeTrue();
    }

    [Fact]
    public async Task StalePreview_IsNotStored_WhenTheStoreWasClearedMeanwhile()
    {
        var path = Path.Combine(_tempDirectory, "a.dll");
        File.WriteAllText(path, "x");
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var store = new ResultsStore(_events, new FakeDecompiler(_ =>
        {
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(10));
            return "late";
        }));

        var pending = store.LoadPreviewAsync(path);
        entered.Wait(TimeSpan.FromSeconds(10)).ShouldBeTrue();
        store.Clear();
        release.Set();
        await pending;

        store.GetSnapshot().HasPreview.ShouldBeFalse();
        store.GetPreview().Text.ShouldBeEmpty();
    }

    // ---- UI-050: an unrenamed parent is a container, not a rename ----

    [Fact]
    public async Task UnrenamedParent_NotShownAsRename()
    {
        // The type has no Type: entry: only its members were renamed.
        await LoadAsync(new Dictionary<string, string>
        {
            ["Method:Ns.Plain.Foo(System.Void ())"] = "m1",
            ["Field:Ns.Plain.Bar"] = "f1"
        });

        var parent = Roots().Items.ShouldHaveSingleItem();

        parent.OriginalName.ShouldBe("Ns.Plain");
        parent.IsContainer.ShouldBeTrue();
        parent.ObfuscatedName.ShouldBeNull();
        parent.ChildCount.ShouldBe(2);
        _store.Query(null, parent.Id, 0, 10).Items.ShouldAllBe(c => !c.IsContainer && c.ObfuscatedName != null);
    }

    [Fact]
    public async Task RenamedParent_IsNotAContainer()
    {
        await LoadAsync(new Dictionary<string, string>
        {
            ["Type:Ns.Real"] = "a",
            ["Field:Ns.Real.Bar"] = "c"
        });

        var parent = Roots().Items.ShouldHaveSingleItem();

        parent.IsContainer.ShouldBeFalse();
        parent.ObfuscatedName.ShouldBe("a");
    }

    [Fact]
    public async Task TypeMappedToItself_IsAContainer()
    {
        await LoadAsync(new Dictionary<string, string>
        {
            ["Type:Same"] = "Same",
            ["Field:Same.Bar"] = "c"
        });

        var parent = Roots().Items.ShouldHaveSingleItem();

        parent.IsContainer.ShouldBeTrue();
        parent.ObfuscatedName.ShouldBeNull();
    }

    [Fact]
    public async Task MemberListedBeforeItsType_StillBecomesARenamedParent()
    {
        await LoadAsync(new Dictionary<string, string>
        {
            ["Field:Late.Bar"] = "c",
            ["Type:Late"] = "z"
        });

        var parent = Roots().Items.ShouldHaveSingleItem();

        parent.IsContainer.ShouldBeFalse();
        parent.ObfuscatedName.ShouldBe("z");
        parent.ChildCount.ShouldBe(1);
    }

    // ---- UI-056: paged symbols ----

    [Fact]
    public async Task Symbols_Query_IsPaged()
    {
        var map = new Dictionary<string, string>();
        for (var i = 0; i < 250; i++)
            map[$"Type:Ns.T{i:D3}"] = $"o{i}";
        map["Type:Ns.Special"] = "zz";
        await LoadAsync(map);

        var first = _store.Query(null, null, 0, 100);
        var second = _store.Query(null, null, 100, 100);
        var tail = _store.Query(null, null, 200, 100);

        first.Total.ShouldBe(251);
        first.Items.Count.ShouldBe(100);
        first.Offset.ShouldBe(0);
        second.Items.Count.ShouldBe(100);
        second.Offset.ShouldBe(100);
        second.Items[0].OriginalName.ShouldBe("Ns.T100");
        tail.Items.Count.ShouldBe(51);
        first.Items.Concat(second.Items).Concat(tail.Items).Select(n => n.Id).Distinct().Count().ShouldBe(251);
        _store.Query(null, null, 300, 100).Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Symbols_Query_SearchFiltersTheParsedModel_AndPages()
    {
        var map = new Dictionary<string, string>();
        for (var i = 0; i < 50; i++)
            map[$"Type:Ns.Alpha{i}"] = $"a{i}";
        for (var i = 0; i < 5; i++)
            map[$"Type:Ns.Beta{i}"] = $"b{i}";
        await LoadAsync(map);

        var page = _store.Query("beta", null, 0, 3);

        page.Total.ShouldBe(5);
        page.Items.Count.ShouldBe(3);
        page.Items.ShouldAllBe(n => n.OriginalName.Contains("Beta"));
        _store.Query("beta", null, 3, 3).Items.Count.ShouldBe(2);
        page.TotalSymbols.ShouldBe(55);
    }

    [Fact]
    public async Task Symbols_Query_PagesChildrenOfAParent()
    {
        var map = new Dictionary<string, string> { ["Type:Big"] = "b" };
        for (var i = 0; i < 30; i++)
            map[$"Field:Big.F{i:D2}"] = $"f{i}";
        await LoadAsync(map);

        var parent = Roots().Items.ShouldHaveSingleItem();
        var page = _store.Query(null, parent.Id, 10, 10);

        parent.ChildCount.ShouldBe(30);
        page.Total.ShouldBe(30);
        page.ParentId.ShouldBe(parent.Id);
        page.Items.Count.ShouldBe(10);
        page.Items[0].OriginalName.ShouldBe("F10");
    }

    [Fact]
    public async Task Symbols_Query_ClampsOffsetAndLimit()
    {
        await LoadAsync(new Dictionary<string, string> { ["Type:A"] = "a", ["Type:B"] = "b" });

        _store.Query(null, null, -5, 0).Items.Count.ShouldBe(2);
        _store.Query(null, null, 0, int.MaxValue).Items.Count.ShouldBe(2);
        _store.Query(null, "nope", 0, 10).Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Symbols_QueryAsync_ReturnsThePage()
    {
        await LoadAsync(new Dictionary<string, string> { ["Type:A"] = "a" });

        var page = await _store.QueryAsync(null, null, 0, 10);

        page.Items.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task QuerySymbolsHandler_AnswersThePage()
    {
        await LoadAsync(new Dictionary<string, string> { ["Type:A"] = "a", ["Type:B"] = "b" });

        var page = await new QuerySymbolsRequestHandler(_store)
            .Handle(new QuerySymbolsRequest("B", null, 0, 10), CancellationToken.None);

        page.Items.ShouldHaveSingleItem().OriginalName.ShouldBe("B");
    }

    [Fact]
    public async Task LoadResults_ParsesOnceOffTheCallingThread()
    {
        var map = new Dictionary<string, string>();
        for (var i = 0; i < 5000; i++)
            map[$"Type:Ns.T{i}"] = $"o{i}";

        var load = _store.LoadResultsAsync(map, new ObfuscationStatistics(), TimeSpan.Zero);
        typeof(ResultsStore).GetMethod(nameof(ResultsStore.LoadResultsAsync))!.ReturnType.ShouldBe(typeof(Task));
        await load;

        _store.Query(null, null, 0, 1).TotalSymbols.ShouldBe(5000);
    }

    [Fact]
    public async Task LoadResults_CopiesTheMap_SoALaterEditDoesNotChangeIt()
    {
        var map = new Dictionary<string, string> { ["Type:A"] = "a" };
        await LoadAsync(map);
        map["Type:B"] = "b";

        _store.GetSymbolMap().Count.ShouldBe(1);
        Roots().Items.Count.ShouldBe(1);
    }

    // ---- UI-059: an explicit before-run state ----

    [Fact]
    public void Results_BeforeAnyRun_ReportsNotRunState()
    {
        var snapshot = _store.GetSnapshot();

        snapshot.State.ShouldBe(ResultsState.NotRun);
        snapshot.Groups.ShouldBeEmpty();
        snapshot.HasReport.ShouldBeFalse();
        snapshot.HasPreview.ShouldBeFalse();
    }

    [Fact]
    public async Task State_FollowsTheRun()
    {
        _store.BeginRun();
        _store.GetSnapshot().State.ShouldBe(ResultsState.Running);

        await LoadAsync(new Dictionary<string, string>());
        _store.GetSnapshot().State.ShouldBe(ResultsState.Finished);

        _store.Clear();
        _store.GetSnapshot().State.ShouldBe(ResultsState.NotRun);
    }

    [Fact]
    public async Task FinishedRunWithNoSymbols_IsNotTheNotRunState()
    {
        await LoadAsync(new Dictionary<string, string>());

        var snapshot = _store.GetSnapshot();

        snapshot.State.ShouldBe(ResultsState.Finished);
        Roots().HasNoSymbols.ShouldBeTrue();
    }

    [Fact]
    public async Task BeginRun_DropsThePreviousResults()
    {
        await LoadAsync(new Dictionary<string, string> { ["Type:A"] = "a" }, new ObfuscationStatistics { TypesRenamed = 1 });
        _store.SetReport(new ObfuscationReport());

        _store.BeginRun();

        var snapshot = _store.GetSnapshot();
        snapshot.State.ShouldBe(ResultsState.Running);
        snapshot.Groups.ShouldBeEmpty();
        snapshot.HasReport.ShouldBeFalse();
        Roots().Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetSummaryHandler_AnswersTheSnapshot()
    {
        var snapshot = await new GetResultsSummaryRequestHandler(_store)
            .Handle(new GetResultsSummaryRequest(), CancellationToken.None);

        snapshot.State.ShouldBe(ResultsState.NotRun);
    }

    private sealed class FakeDecompiler(Func<string, string> decompile) : IAssemblyDecompiler
    {
        public string Decompile(string assemblyPath) => decompile(assemblyPath);
    }

    private sealed class FakeReports : IReportService
    {
        public List<(string Path, ReportFormat Format)> Generated { get; } = [];

        public Exception? Failure { get; set; }

        public Task GenerateReportAsync(ObfuscationReport report, string outputPath, ReportFormat format, CancellationToken cancellationToken = default)
        {
            if (Failure is not null)
                return Task.FromException(Failure);

            Generated.Add((outputPath, format));
            return Task.CompletedTask;
        }

        public string GetFileExtension(ReportFormat format) => format == ReportFormat.Json ? ".json" : ".html";

        public ObfuscationReport BuildReport(PipelineContext context, ObfuscationResult result, ObfySettings settings) => throw new NotSupportedException();

        public ObfuscationReport BuildReport(ObfuscationResult result, ObfySettings settings) => throw new NotSupportedException();

        public ObfuscationReport BuildCombinedReport(IReadOnlyList<ObfuscationResult> results, ObfySettings settings) => throw new NotSupportedException();
    }
}
