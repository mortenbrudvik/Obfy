using Shouldly;
using static Obfy.UI.Tests.Shell.ShutdownCoordinatorTests;

namespace Obfy.UI.Tests;

public class ReviewFixesWave2UiTests
{
    [Fact]
    public async Task RestartAsync_WhileFlushingPreferences_IsNotPreemptedByTryApproveWithoutAskingOrQuit()
    {
        var f = new Fixture();
        var shutdown = f.Build();

        var restart = shutdown.RestartAsync();
        await Task.Delay(50);

        shutdown.TryApproveWithoutAsking().ShouldBeFalse();
        (await shutdown.PrepareToCloseAsync()).ShouldBeFalse();
        await shutdown.QuitAsync();
        f.Shutdowns.ShouldBe(0);
        f.NewInstances.ShouldBe(0);

        f.PreferencesWritten.SetResult();
        await restart;

        f.Order.ShouldBe(["flush-start", "flush-done", "new-instance", "shutdown"]);
        f.NewInstances.ShouldBe(1);
        f.Shutdowns.ShouldBe(1);
    }

    [Fact]
    public async Task RestartAsync_WhenNewInstanceFails_ClearsRestartingFlagSoLaterCloseWorks()
    {
        var f = new Fixture { NewInstanceStarts = false };
        var shutdown = f.Build();
        f.PreferencesWritten.SetResult();

        await shutdown.RestartAsync();

        f.Shutdowns.ShouldBe(0);
        shutdown.Approved.ShouldBeFalse();
        shutdown.TryApproveWithoutAsking().ShouldBeTrue();
        shutdown.Approved.ShouldBeTrue();
    }

    [Fact]
    public async Task TryApproveWithoutAsking_WhilePreparingToCloseForRestart_ReturnsFalse()
    {
        var f = new Fixture { Running = true };
        var shutdown = f.Build();
        f.PreferencesWritten.SetResult();

        var restart = shutdown.RestartAsync();
        await Task.Delay(50);

        // Even if the underlying run flag clears before RunEnded completes, _preparing prevents preemption.
        f.Running = false;
        shutdown.TryApproveWithoutAsking().ShouldBeFalse();
        f.Shutdowns.ShouldBe(0);
        f.NewInstances.ShouldBe(0);

        f.RunEnded.SetResult();
        await restart;

        f.Order.ShouldBe(["cancel", "flush-start", "flush-done", "new-instance", "shutdown"]);
        f.NewInstances.ShouldBe(1);
        f.Shutdowns.ShouldBe(1);
    }
}
