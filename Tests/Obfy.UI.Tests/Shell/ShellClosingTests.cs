using System.Windows.Input;
using Autofac;
using Obfy.UI.Services;
using Obfy.UI.ViewModels;
using Obfy.UI.Views;
using Shouldly;
using static Obfy.UI.Tests.Shell.ShutdownCoordinatorTests;

namespace Obfy.UI.Tests.Shell;

/// <summary>
/// The real shell window and view model with a stand-in <see cref="ShutdownCoordinator"/>: closing the window, Quit and
/// Restart must go through it. The window had no Closing handler at all, so closing it mid-run killed the run.
/// </summary>
public class ShellClosingTests
{
    private static IContainer Build(Fixture fixture) =>
        ShellHarness.BuildContainer(b => b.RegisterInstance(fixture.Build()).AsSelf());

    [Fact]
    public void ClosingTheWindow_DuringARun_AsksFirst_AndStaysOpenWhenDeclined()
    {
        ShellHarness.OnSta(() =>
        {
            ShellHarness.UseShellResources();
            var fixture = new Fixture { Running = true, UserSaysYes = false };
            using var container = Build(fixture);
            var window = container.Resolve<ShellWindow>();
            var closed = false;
            window.Closed += (_, _) => closed = true;

            window.Close();
            ShellHarness.PumpUntil(() => closed, 300);

            fixture.Asked.ShouldBe(1);
            fixture.Cancelled.ShouldBe(0);
            closed.ShouldBeFalse();

            // A later close, with the run over, goes through without a question.
            fixture.Running = false;
            window.Close();
            closed.ShouldBeTrue();
            fixture.Asked.ShouldBe(1);
            return true;
        });
    }

    [Fact]
    public void ClosingTheWindow_DuringARun_CancelsIt_AndClosesOnlyOnceTheRunHasStopped()
    {
        ShellHarness.OnSta(() =>
        {
            ShellHarness.UseShellResources();
            var fixture = new Fixture { Running = true };
            using var container = Build(fixture);
            var window = container.Resolve<ShellWindow>();
            var closed = false;
            window.Closed += (_, _) => closed = true;

            window.Close();
            ShellHarness.PumpUntil(() => closed, 300);

            fixture.Cancelled.ShouldBe(1);
            closed.ShouldBeFalse("the run has not stopped yet");

            fixture.RunEnded.SetResult();
            ShellHarness.PumpUntil(() => closed);

            closed.ShouldBeTrue();
            fixture.Asked.ShouldBe(1);
            return true;
        });
    }

    [Fact]
    public void ClosingTheWindow_WithNoRun_ClosesAtOnce_WithoutAsking()
    {
        ShellHarness.OnSta(() =>
        {
            ShellHarness.UseShellResources();
            var fixture = new Fixture();
            using var container = Build(fixture);
            var window = container.Resolve<ShellWindow>();
            var closed = false;
            window.Closed += (_, _) => closed = true;

            window.Close();

            closed.ShouldBeTrue();
            fixture.Asked.ShouldBe(0);
            return true;
        });
    }

    [Fact]
    public void ClosingTheWindow_WithNoRun_RefusesNewRunsAtOnce()
    {
        ShellHarness.OnSta(() =>
        {
            ShellHarness.UseShellResources();
            var fixture = new Fixture();
            using var container = Build(fixture);
            var window = container.Resolve<ShellWindow>();

            window.Close();

            // A run whose files are still being checked would otherwise start while the process exits.
            fixture.DoorClosed.ShouldBeTrue();
            return true;
        });
    }

    [Fact]
    public void ClosingTheWindow_DuringARun_Declined_LeavesRunsPossible()
    {
        ShellHarness.OnSta(() =>
        {
            ShellHarness.UseShellResources();
            var fixture = new Fixture { Running = true, UserSaysYes = false };
            using var container = Build(fixture);
            var window = container.Resolve<ShellWindow>();
            var closed = false;
            window.Closed += (_, _) => closed = true;

            window.Close();
            ShellHarness.PumpUntil(() => fixture.Asked > 0 || closed, 300);

            closed.ShouldBeFalse();
            fixture.DoorClosed.ShouldBeFalse();
            return true;
        });
    }

    [Fact]
    public void QuitOnTheFailurePanel_DuringARun_AsksFirst_AndDoesNothingWhenDeclined()
    {
        ShellHarness.OnSta(() =>
        {
            var fixture = new Fixture { Running = true, UserSaysYes = false };
            using var container = Build(fixture);
            var viewModel = container.Resolve<ShellViewModel>();

            ((ICommand)viewModel.QuitCommand).Execute(null);
            ShellHarness.PumpUntil(() => fixture.Asked > 0, 300);

            fixture.Asked.ShouldBe(1);
            fixture.Shutdowns.ShouldBe(0);
            return true;
        });
    }
}
