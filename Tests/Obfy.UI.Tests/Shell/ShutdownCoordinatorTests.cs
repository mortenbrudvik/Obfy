using Microsoft.Extensions.Logging.Abstractions;
using Obfy.UI.Services;
using Shouldly;

namespace Obfy.UI.Tests.Shell;

/// <summary>
/// Closing, quitting or restarting while a run goes used to end the process at once: no cancel, no wait, no question, so a
/// half-written output could be left behind. These pin the rule that now sits in front of all three.
/// </summary>
public class ShutdownCoordinatorTests
{
    internal sealed class Fixture
    {
        public bool Running;

        /// <summary>New runs are refused: the shell has agreed to go away.</summary>
        public bool DoorClosed;

        /// <summary>Whether new runs were already refused at the moment the run was cancelled.</summary>
        public bool DoorClosedAtCancel;

        public bool UserSaysYes = true;
        public int Asked;
        public int Cancelled;
        public int Shutdowns;
        public int NewInstances;
        public bool NewInstanceStarts = true;
        public readonly List<string> Order = [];
        public TaskCompletionSource RunEnded = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource PreferencesWritten = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TimeSpan RunTimeout = TimeSpan.FromSeconds(5);
        public TimeSpan PreferenceTimeout = TimeSpan.FromSeconds(5);

        public ShutdownCoordinator Build() => new(
            isRunning: () => Running,
            cancelRun: () =>
            {
                Cancelled++;
                DoorClosedAtCancel = DoorClosed;
                Order.Add("cancel");
            },
            runCompletion: () => RunEnded.Task,
            confirmCancelRun: () =>
            {
                Asked++;
                return UserSaysYes;
            },
            tryRefuseNewRuns: () =>
            {
                if (Running)
                    return false;
                DoorClosed = true;
                return true;
            },
            refuseNewRuns: () => DoorClosed = true,
            allowNewRuns: () => DoorClosed = false,
            flushPreferences: async () =>
            {
                Order.Add("flush-start");
                await PreferencesWritten.Task;
                Order.Add("flush-done");
            },
            startNewInstance: () =>
            {
                Order.Add("new-instance");
                NewInstances++;
                return NewInstanceStarts;
            },
            shutdown: () =>
            {
                Order.Add("shutdown");
                Shutdowns++;
            },
            runStopTimeout: RunTimeout,
            preferencesTimeout: PreferenceTimeout,
            logger: NullLogger.Instance);
    }

    [Fact]
    public async Task NoRun_ClosesAtOnce_WithoutAsking()
    {
        var f = new Fixture();
        var shutdown = f.Build();

        (await shutdown.PrepareToCloseAsync()).ShouldBeTrue();

        f.Asked.ShouldBe(0);
        f.Cancelled.ShouldBe(0);
        shutdown.Approved.ShouldBeTrue();
    }

    [Fact]
    public async Task ARunIsGoing_AndTheUserDeclines_NothingIsCancelledAndTheWindowStaysOpen()
    {
        var f = new Fixture { Running = true, UserSaysYes = false };
        var shutdown = f.Build();

        (await shutdown.PrepareToCloseAsync()).ShouldBeFalse();

        f.Asked.ShouldBe(1);
        f.Cancelled.ShouldBe(0);
        shutdown.Approved.ShouldBeFalse();
    }

    [Fact]
    public async Task ARunIsGoing_AndTheUserAgrees_TheRunIsCancelled_AndWaitedFor_BeforeClosing()
    {
        var f = new Fixture { Running = true };
        var shutdown = f.Build();

        var closing = shutdown.PrepareToCloseAsync();
        await Task.Delay(100);

        // Cancelled, but the run has not stopped yet: closing now could cut a file short.
        f.Cancelled.ShouldBe(1);
        closing.IsCompleted.ShouldBeFalse();

        f.RunEnded.SetResult();
        (await closing).ShouldBeTrue();
        shutdown.Approved.ShouldBeTrue();
    }

    [Fact]
    public async Task ARunThatWillNotStop_IsWaitedForOnlyAWhile()
    {
        var f = new Fixture { Running = true, RunTimeout = TimeSpan.FromMilliseconds(150) };
        var shutdown = f.Build();

        // The run never ends: a hung run must not keep the window open for ever.
        (await shutdown.PrepareToCloseAsync()).ShouldBeTrue();

        f.Cancelled.ShouldBe(1);
    }

    [Fact]
    public async Task TheRunEndsWhileTheQuestionIsOpen_NothingIsCancelled()
    {
        var f = new Fixture { Running = true };
        var shutdown = new ShutdownCoordinator(
            isRunning: () => f.Running,
            cancelRun: () => f.Cancelled++,
            runCompletion: () => f.RunEnded.Task,
            confirmCancelRun: () =>
            {
                f.Running = false;
                return true;
            },
            tryRefuseNewRuns: () => !f.Running,
            refuseNewRuns: () => { },
            allowNewRuns: () => { },
            flushPreferences: () => Task.CompletedTask,
            startNewInstance: () => true,
            shutdown: () => { },
            runStopTimeout: TimeSpan.FromSeconds(5),
            preferencesTimeout: TimeSpan.FromSeconds(5),
            logger: NullLogger.Instance);

        (await shutdown.PrepareToCloseAsync()).ShouldBeTrue();

        f.Cancelled.ShouldBe(0);
    }

    [Fact]
    public async Task ASecondRequest_WhileTheFirstWaits_IsIgnored_NotAskedAgain()
    {
        var f = new Fixture { Running = true };
        var shutdown = f.Build();

        var first = shutdown.PrepareToCloseAsync();
        await Task.Delay(100);
        (await shutdown.PrepareToCloseAsync()).ShouldBeFalse();

        f.Asked.ShouldBe(1);
        f.RunEnded.SetResult();
        (await first).ShouldBeTrue();
    }

    [Fact]
    public async Task Quit_DuringARun_AsksFirst_AndDoesNotShutDownUntilTheRunStopped()
    {
        var f = new Fixture { Running = true };
        var shutdown = f.Build();

        var quit = shutdown.QuitAsync();
        await Task.Delay(100);
        f.Shutdowns.ShouldBe(0);

        f.RunEnded.SetResult();
        await quit;

        f.Asked.ShouldBe(1);
        f.Order.ShouldBe(["cancel", "shutdown"]);
    }

    [Fact]
    public async Task Quit_DuringARun_Declined_DoesNotShutDown()
    {
        var f = new Fixture { Running = true, UserSaysYes = false };
        var shutdown = f.Build();

        await shutdown.QuitAsync();

        f.Shutdowns.ShouldBe(0);
    }

    [Fact]
    public async Task Quit_WithoutARun_ShutsDown()
    {
        var f = new Fixture();
        var shutdown = f.Build();

        await shutdown.QuitAsync();

        f.Shutdowns.ShouldBe(1);
        f.Asked.ShouldBe(0);
    }

    [Fact]
    public async Task Restart_WritesThePreferencesBeforeTheNewProcessStarts()
    {
        var f = new Fixture();
        var shutdown = f.Build();

        var restart = shutdown.RestartAsync();
        await Task.Delay(100);

        // The new Obfy reads ui-preferences.json as it starts: the old one's last write must already be on disk.
        f.NewInstances.ShouldBe(0);
        f.PreferencesWritten.SetResult();
        await restart;

        f.Order.ShouldBe(["flush-start", "flush-done", "new-instance", "shutdown"]);
    }

    [Fact]
    public async Task Restart_WithAPreferenceWriteThatHangs_StartsTheNewProcessAnyway()
    {
        var f = new Fixture { PreferenceTimeout = TimeSpan.FromMilliseconds(150) };
        var shutdown = f.Build();

        await shutdown.RestartAsync();

        f.NewInstances.ShouldBe(1);
        f.Shutdowns.ShouldBe(1);
    }

    [Fact]
    public async Task Restart_ThatCannotStartANewProcess_StaysOpen_AndAsksAgainNextTime()
    {
        var f = new Fixture { NewInstanceStarts = false, Running = true };
        var shutdown = f.Build();
        f.PreferencesWritten.SetResult();
        f.RunEnded.SetResult();

        await shutdown.RestartAsync();

        f.Shutdowns.ShouldBe(0);
        shutdown.Approved.ShouldBeFalse();
    }

    [Fact]
    public async Task Restart_DuringARun_Declined_StartsNothing()
    {
        var f = new Fixture { Running = true, UserSaysYes = false };
        var shutdown = f.Build();

        await shutdown.RestartAsync();

        f.NewInstances.ShouldBe(0);
        f.Shutdowns.ShouldBe(0);
        f.Order.ShouldBeEmpty();
    }

    // ---- New runs are refused once the shell has agreed to go (a Start still checking its files must not become a run) ----

    [Fact]
    public async Task NoRun_ApprovingTheCloseRefusesNewRuns()
    {
        var f = new Fixture();
        var shutdown = f.Build();

        (await shutdown.PrepareToCloseAsync()).ShouldBeTrue();

        f.DoorClosed.ShouldBeTrue();
    }

    [Fact]
    public void NoRun_ApprovingWithoutAskingRefusesNewRuns_AtTheSameMoment()
    {
        var f = new Fixture();
        var shutdown = f.Build();

        shutdown.TryApproveWithoutAsking().ShouldBeTrue();

        shutdown.Approved.ShouldBeTrue();
        f.DoorClosed.ShouldBeTrue();
    }

    [Fact]
    public void ARunIsGoing_ThereIsNothingToApproveWithoutAsking_AndRunsStayPossible()
    {
        var f = new Fixture { Running = true };
        var shutdown = f.Build();

        shutdown.TryApproveWithoutAsking().ShouldBeFalse();

        shutdown.Approved.ShouldBeFalse();
        f.DoorClosed.ShouldBeFalse();
    }

    [Fact]
    public async Task ARunIsGoing_AndTheUserDeclines_RunsStayPossible()
    {
        var f = new Fixture { Running = true, UserSaysYes = false };
        var shutdown = f.Build();

        (await shutdown.PrepareToCloseAsync()).ShouldBeFalse();

        f.DoorClosed.ShouldBeFalse();
    }

    [Fact]
    public async Task ARunIsGoing_AndTheUserAgrees_NewRunsAreRefusedBeforeTheRunIsCancelled()
    {
        var f = new Fixture { Running = true };
        var shutdown = f.Build();

        var closing = shutdown.PrepareToCloseAsync();
        await Task.Delay(100);

        // A Start that was still checking when the user said yes must not become the next run behind the cancel.
        f.Cancelled.ShouldBe(1);
        f.DoorClosedAtCancel.ShouldBeTrue();

        f.RunEnded.SetResult();
        (await closing).ShouldBeTrue();
        f.DoorClosed.ShouldBeTrue();
    }

    [Fact]
    public async Task Restart_ThatCannotStartANewProcess_AllowsNewRunsAgain()
    {
        var f = new Fixture { NewInstanceStarts = false };
        var shutdown = f.Build();
        f.PreferencesWritten.SetResult();

        await shutdown.RestartAsync();

        f.Shutdowns.ShouldBe(0);
        shutdown.Approved.ShouldBeFalse();
        f.DoorClosed.ShouldBeFalse();
    }
}
