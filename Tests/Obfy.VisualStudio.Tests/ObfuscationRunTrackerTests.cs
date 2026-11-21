using Obfy.VisualStudio.Services;
using Shouldly;

namespace Obfy.VisualStudio.Tests;

public class ObfuscationRunTrackerTests
{
    private const string ProjectA = @"C:\src\A\A.csproj";
    private const string ProjectB = @"C:\src\B\B.csproj";

    [Fact]
    public void Begin_RegistersARunWithAnUncancelledToken_UntilItIsDisposed()
    {
        var tracker = new ObfuscationRunTracker();

        var run = tracker.Begin(ProjectA);

        run.Token.IsCancellationRequested.ShouldBeFalse();
        tracker.ActiveRunCount.ShouldBe(1);

        run.Dispose();

        tracker.ActiveRunCount.ShouldBe(0);
    }

    [Fact]
    public void CancelAll_CancelsEveryRun_AndCountsThem()
    {
        var tracker = new ObfuscationRunTracker();
        using var a = tracker.Begin(ProjectA);
        using var b = tracker.Begin(ProjectB);
        using var unkeyed = tracker.Begin(null);

        tracker.CancelAll().ShouldBe(3);

        a.Token.IsCancellationRequested.ShouldBeTrue();
        b.Token.IsCancellationRequested.ShouldBeTrue();
        unkeyed.Token.IsCancellationRequested.ShouldBeTrue();
    }

    [Fact]
    public void CancelAll_CountsOnlyRunsItCancelsNow()
    {
        var tracker = new ObfuscationRunTracker();
        using var run = tracker.Begin(ProjectA);

        tracker.CancelAll().ShouldBe(1);
        tracker.CancelAll().ShouldBe(0);
    }

    [Fact]
    public void CancelProject_CancelsOnlyThatProjectsRuns_IgnoringPathCase()
    {
        var tracker = new ObfuscationRunTracker();
        using var a1 = tracker.Begin(ProjectA);
        using var a2 = tracker.Begin(ProjectA);
        using var b = tracker.Begin(ProjectB);

        tracker.CancelProject(ProjectA.ToUpperInvariant()).ShouldBe(2);

        a1.Token.IsCancellationRequested.ShouldBeTrue();
        a2.Token.IsCancellationRequested.ShouldBeTrue();
        b.Token.IsCancellationRequested.ShouldBeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(@"C:\src\Other\Other.csproj")]
    public void CancelProject_WithoutAMatchingProject_CancelsNothing(string? key)
    {
        var tracker = new ObfuscationRunTracker();
        using var unkeyed = tracker.Begin(null);
        using var a = tracker.Begin(ProjectA);

        tracker.CancelProject(key).ShouldBe(0);

        unkeyed.Token.IsCancellationRequested.ShouldBeFalse();
        a.Token.IsCancellationRequested.ShouldBeFalse();
    }

    [Fact]
    public void Dispose_ReleasesTheCancellationTokenSource()
    {
        var tracker = new ObfuscationRunTracker();
        var run = tracker.Begin(ProjectA);
        var token = run.Token;

        run.Dispose();

        // WaitHandle is created on demand and refuses a disposed source.
        Should.Throw<ObjectDisposedException>(() => _ = token.WaitHandle);
    }

    [Fact]
    public void Dispose_IsIdempotent_AndAFinishedRunIsNotCancelledLater()
    {
        var tracker = new ObfuscationRunTracker();
        var run = tracker.Begin(ProjectA);

        run.Dispose();
        Should.NotThrow(() => run.Dispose());

        Should.NotThrow(() => tracker.CancelAll().ShouldBe(0));
        Should.NotThrow(() => tracker.CancelProject(ProjectA).ShouldBe(0));
        run.Token.IsCancellationRequested.ShouldBeFalse();
    }

    [Fact]
    public void Shutdown_CancelsRunningRuns_AndRunsThatBeginAfterwards()
    {
        var tracker = new ObfuscationRunTracker();
        using var running = tracker.Begin(ProjectA);

        tracker.Shutdown();
        using var late = tracker.Begin(ProjectB);

        running.Token.IsCancellationRequested.ShouldBeTrue();
        late.Token.IsCancellationRequested.ShouldBeTrue();
    }

    [Fact]
    public void CancellingWhileRunsEndConcurrently_NeverThrows()
    {
        var tracker = new ObfuscationRunTracker();

        Should.NotThrow(() =>
            Parallel.For(0, 400, i =>
            {
                var run = tracker.Begin(ProjectA);
                if (i % 2 == 0)
                    tracker.CancelAll();
                else
                    tracker.CancelProject(ProjectA);
                run.Dispose();
            }));

        tracker.ActiveRunCount.ShouldBe(0);
    }
}
