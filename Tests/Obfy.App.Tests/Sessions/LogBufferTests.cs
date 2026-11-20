using System.Text.RegularExpressions;
using Obfy.App.Features.Log;
using Obfy.App.Models;
using Obfy.App.Sessions;
using Obfy.App.Tests.Common;
using Shouldly;

namespace Obfy.App.Tests.Sessions;

public class LogBufferTests
{
    /// <summary>Long enough that the batch timer never fires inside a test: tests flush by hand.</summary>
    private static readonly TimeSpan NeverFires = TimeSpan.FromHours(1);

    private readonly FakeEvents _events = new();

    private LogBuffer Create() => new(_events, NeverFires);

    [Fact]
    public void Add_StoresEntryWithMessageAndLevel()
    {
        using var log = Create();

        log.Add(LogLevel.Warning, "hello");

        var lines = log.Get();
        lines.Count.ShouldBe(1);
        lines[0].Message.ShouldBe("hello");
        lines[0].Level.ShouldBe(LogLevel.Warning);
    }

    [Fact]
    public void Add_Info_StoresInfoEntry()
    {
        using var log = Create();

        log.Add(LogLevel.Info, "ready");

        var line = log.Get().ShouldHaveSingleItem();
        line.Level.ShouldBe(LogLevel.Info);
        line.Message.ShouldBe("ready");
    }

    [Fact]
    public void Add_StampsTimeOfDayWithMilliseconds_AndIncreasingIds()
    {
        using var log = Create();

        log.Add(LogLevel.Info, "one");
        log.Add(LogLevel.Info, "two");

        var lines = log.Get();
        Regex.IsMatch(lines[0].Timestamp, @"^\d{2}:\d{2}:\d{2}\.\d{3}$").ShouldBeTrue(lines[0].Timestamp);
        lines[1].Id.ShouldBeGreaterThan(lines[0].Id);
    }

    [Fact]
    public void Clear_RemovesAllEntries()
    {
        using var log = Create();
        log.Add(LogLevel.Info, "one");
        log.Add(LogLevel.Error, "two");

        log.Clear();

        log.Get().ShouldBeEmpty();
    }

    [Fact]
    public void Clear_KeepsIdsIncreasing_SoALateLineIsNeverTakenForAnEarlierOne()
    {
        using var log = Create();
        log.Add(LogLevel.Info, "one");
        var first = log.Get()[0].Id;

        log.Clear();
        log.Add(LogLevel.Info, "two");

        log.Get()[0].Id.ShouldBeGreaterThan(first);
    }

    [Fact]
    public void Get_ReturnsACopy_ThatLaterAddsDoNotChange()
    {
        using var log = Create();
        log.Add(LogLevel.Info, "one");
        var before = log.Get();

        log.Add(LogLevel.Info, "two");

        before.Count.ShouldBe(1);
    }

    [Fact]
    public void NewRun_InsertsSeparator_WhenNonEmpty()
    {
        using var log = Create();
        log.Add(LogLevel.Success, "run one done");

        log.StartRunSeparator();
        log.Add(LogLevel.Info, "run two");

        var lines = log.Get();
        lines.Select(l => l.Message).ShouldBe(["run one done", "———", "run two"]);
        lines[1].Level.ShouldBe(LogLevel.Info);
    }

    [Fact]
    public void NewRun_InsertsNothing_WhenEmpty()
    {
        using var log = Create();

        log.StartRunSeparator();

        log.Get().ShouldBeEmpty();
    }

    [Fact]
    public void NewRun_InsertsNothing_AfterClear()
    {
        using var log = Create();
        log.Add(LogLevel.Info, "one");
        log.Clear();

        log.StartRunSeparator();

        log.Get().ShouldBeEmpty();
    }

    [Fact]
    public void Revision_RisesWithEveryChange()
    {
        using var log = Create();
        var start = log.GetSnapshot().Revision;

        log.Add(LogLevel.Info, "one");
        var afterAdd = log.GetSnapshot().Revision;
        log.StartRunSeparator();
        var afterSeparator = log.GetSnapshot().Revision;
        log.Clear();
        var afterClear = log.GetSnapshot().Revision;

        afterAdd.ShouldBeGreaterThan(start);
        afterSeparator.ShouldBeGreaterThan(afterAdd);
        afterClear.ShouldBeGreaterThan(afterSeparator);
    }

    [Fact]
    public void Add_WithinTheBatchWindow_CoalescesIntoOneAppendedEvent()
    {
        using var log = Create();

        log.Add(LogLevel.Info, "a");
        log.Add(LogLevel.Warning, "b");
        log.Add(LogLevel.Error, "c");

        _events.Published.ShouldBeEmpty();

        log.Flush();

        var published = _events.Published.ShouldHaveSingleItem();
        published.Name.ShouldBe(AppEvents.LogAppended);
        var batch = published.Data.ShouldBeOfType<LogAppended>();
        batch.Lines.Select(l => l.Message).ShouldBe(["a", "b", "c"]);
        batch.Revision.ShouldBe(log.GetSnapshot().Revision);
    }

    [Fact]
    public void Flush_PublishesOnlyWhatIsNewSinceTheLastFlush()
    {
        using var log = Create();
        log.Add(LogLevel.Info, "a");
        log.Flush();

        log.Add(LogLevel.Info, "b");
        log.Flush();

        var batches = _events.Published.Select(p => p.Data.ShouldBeOfType<LogAppended>()).ToList();
        batches.Count.ShouldBe(2);
        batches[1].Lines.Select(l => l.Message).ShouldBe(["b"]);
    }

    [Fact]
    public void Flush_WithNothingPending_PublishesNothing()
    {
        using var log = Create();

        log.Flush();
        log.Add(LogLevel.Info, "a");
        log.Flush();
        log.Flush();

        _events.Published.Count.ShouldBe(1);
    }

    [Fact]
    public void Clear_PublishesClearedSnapshot_AndDropsTheUnsentBatch()
    {
        using var log = Create();
        log.Add(LogLevel.Info, "a");

        log.Clear();
        log.Flush();

        var published = _events.Published.ShouldHaveSingleItem();
        published.Name.ShouldBe(AppEvents.LogCleared);
        var snapshot = published.Data.ShouldBeOfType<LogSnapshot>();
        snapshot.Lines.ShouldBeEmpty();
        snapshot.Revision.ShouldBe(log.GetSnapshot().Revision);
    }

    [Fact]
    public void Clear_WhenAlreadyEmpty_PublishesNothing()
    {
        using var log = Create();

        log.Clear();

        _events.Published.ShouldBeEmpty();
    }

    [Fact]
    public async Task Add_Publishes_OnceTheTimerFires_WithoutAManualFlush()
    {
        var published = new TaskCompletionSource();
        _events.OnPublish = () => published.TrySetResult();
        using var log = new LogBuffer(_events, TimeSpan.FromMilliseconds(10));

        log.Add(LogLevel.Info, "a");

        await published.Task.WaitAsync(TimeSpan.FromSeconds(10));
        _events.Published[0].Name.ShouldBe(AppEvents.LogAppended);
    }

    [Fact]
    public void Add_FromManyThreads_KeepsEveryLine_WithDistinctIncreasingIds()
    {
        using var log = Create();

        Parallel.For(0, 8, t =>
        {
            for (var i = 0; i < 250; i++)
                log.Add(LogLevel.Info, $"{t}-{i}");
        });
        log.Flush();

        var lines = log.Get();
        lines.Count.ShouldBe(2000);
        lines.Select(l => l.Id).ShouldBe(lines.Select(l => l.Id).OrderBy(id => id));
        lines.Select(l => l.Id).Distinct().Count().ShouldBe(2000);
        _events.Published.Sum(p => ((LogAppended)p.Data!).Lines.Count).ShouldBe(2000);
    }

    [Fact]
    public void WithoutAPublisher_StillKeepsTheLog()
    {
        using var log = new LogBuffer(null, NeverFires);

        log.Add(LogLevel.Info, "a");
        log.Flush();
        log.Clear();

        log.Get().ShouldBeEmpty();
    }

    [Fact]
    public async Task GetHandler_ReturnsTheLinesWithTheirRevision()
    {
        using var log = Create();
        log.Add(LogLevel.Error, "boom");

        var snapshot = await new GetLogRequestHandler(log).Handle(new GetLogRequest(), CancellationToken.None);

        snapshot.Lines.ShouldHaveSingleItem().Message.ShouldBe("boom");
        snapshot.Revision.ShouldBe(log.GetSnapshot().Revision);
    }

    [Fact]
    public async Task ClearHandler_EmptiesTheLog_AndAnswersWithTheSnapshot()
    {
        using var log = Create();
        log.Add(LogLevel.Warning, "keep me?");

        var snapshot = await new ClearLogRequestHandler(log).Handle(new ClearLogRequest(), CancellationToken.None);

        log.Get().ShouldBeEmpty();
        snapshot.Lines.ShouldBeEmpty();
    }
}
