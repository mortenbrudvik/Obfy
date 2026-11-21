using Microsoft.Web.WebView2.Core;
using Obfy.WebView;
using Shouldly;

namespace Obfy.UI.Tests.Shell;

/// <summary>
/// The reload allowance of <see cref="WebViewHandler"/>: it used to be a counter that was only ever incremented, so any
/// three renderer failures over the life of the process (each one recovered) made the fourth show the permanent failure panel.
/// </summary>
public class ReloadBudgetTests
{
    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    private static readonly TimeSpan Stable = TimeSpan.FromMinutes(2);

    [Fact]
    public void FailuresInQuickSuccession_UseUpTheAllowance_AndGiveUp()
    {
        var clock = new ManualClock();
        var budget = new ReloadBudget(Stable, clock);

        for (var i = 0; i < WebViewHandler.MaxReloads; i++)
        {
            WebViewHandler.RespondTo(CoreWebView2ProcessFailedKind.RenderProcessExited, budget.Used).ShouldBe(ProcessFailureResponse.Reload);
            budget.Record();
            clock.Advance(TimeSpan.FromSeconds(5));
        }

        WebViewHandler.RespondTo(CoreWebView2ProcessFailedKind.RenderProcessExited, budget.Used).ShouldBe(ProcessFailureResponse.GiveUp);
    }

    [Fact]
    public void FailuresThatEachRecovered_AreForgiven_SoTheFourthStillReloads()
    {
        var clock = new ManualClock();
        var budget = new ReloadBudget(Stable, clock);

        // Five failures an hour apart: the page ran fine in between, so none of them may count against the next.
        for (var i = 0; i < WebViewHandler.MaxReloads + 2; i++)
        {
            WebViewHandler.RespondTo(CoreWebView2ProcessFailedKind.RenderProcessExited, budget.Used)
                .ShouldBe(ProcessFailureResponse.Reload, $"failure {i + 1}");
            budget.Record();
            clock.Advance(TimeSpan.FromHours(1));
        }
    }

    [Fact]
    public void ALoopOfLoadThenCrash_StillGivesUp()
    {
        // A page whose renderer dies shortly after every successful load must not be reloaded forever: "stable" is
        // measured in time since the last failure, not in a navigation having completed.
        var clock = new ManualClock();
        var budget = new ReloadBudget(Stable, clock);

        for (var i = 0; i < WebViewHandler.MaxReloads; i++)
        {
            budget.Record();
            clock.Advance(TimeSpan.FromSeconds(30));
        }

        budget.Used.ShouldBe(WebViewHandler.MaxReloads);
        WebViewHandler.RespondTo(CoreWebView2ProcessFailedKind.RenderProcessUnresponsive, budget.Used).ShouldBe(ProcessFailureResponse.GiveUp);
    }

    [Fact]
    public void AFailureAfterAStableSpell_StartsTheCountOver()
    {
        var clock = new ManualClock();
        var budget = new ReloadBudget(Stable, clock);
        budget.Record();
        budget.Record();
        budget.Used.ShouldBe(2);

        clock.Advance(Stable);

        budget.Used.ShouldBe(0);
        budget.Record();
        budget.Used.ShouldBe(1);
    }
}
