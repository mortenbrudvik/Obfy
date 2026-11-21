using Obfy.App.Models;
using Obfy.App.Sessions;
using Obfy.App.Tests.Common;
using Obfy.Core.Models;
using Shouldly;

namespace Obfy.App.Tests.Sessions;

/// <summary>A snapshot must not reach the page after a newer one: the page keeps the newest it has seen.</summary>
public sealed class ResultsStorePublishOrderTests
{
    [Fact]
    public async Task ASlowPublishNeverLetsAnOlderSnapshotArriveAfterANewerOne()
    {
        var events = new HookedEvents();
        var store = new ResultsStore(events);

        using var atGate = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var held = 0;
        events.Before = (_, _) =>
        {
            if (Interlocked.Exchange(ref held, 1) == 0)
            {
                atGate.Set();
                release.Wait(ReviewHarness.Guard);
            }
        };

        var first = Task.Run(() => store.SetReport(new ObfuscationReport()));
        atGate.Wait(ReviewHarness.Guard).ShouldBeTrue();

        var second = Task.Run(() => store.SetPreviewPaths([@"C:\a\one.dll"]));
        await Task.WhenAny(second, Task.Delay(400)); // without a publish lock it is pushed now, ahead of the held one
        release.Set();
        await Task.WhenAll(first, second).WaitAsync(ReviewHarness.Guard);

        var revisions = events.Published
            .Where(e => e.Name == AppEvents.ResultsChanged)
            .Select(e => ((ResultsSnapshot)e.Data!).Revision)
            .ToList();
        revisions.Count.ShouldBe(2);
        revisions.ShouldBe(revisions.OrderBy(r => r));
        store.GetSnapshot().Revision.ShouldBe(revisions.Last());
    }
}
