using Autofac;
using Moq;
using Obfy.App.Features.App;
using Obfy.Interop;
using Obfy.UI.ViewModels;
using Obfy.UI.Views;
using Obfy.WebView;
using Shouldly;

namespace Obfy.UI.Tests.Shell;

public class ShellRevealTests
{
    private static (IContainer Container, List<string> PublishedEvents) BuildWithEventSpy()
    {
        var published = new List<string>();
        var webView = new Mock<IWebViewInterop>();
        webView.Setup(w => w.PostWebMessageAsJson(It.IsAny<string>()))
            .Callback<string>(json => published.Add(json));

        var container = ShellHarness.BuildContainer(b =>
            b.Register(c => new ApiEventAggregator(webView.Object, c.Resolve<IpcLogger>()))
                .AsSelf()
                .SingleInstance());

        return (container, published);
    }

    [Fact]
    public void Preload_WhenNotCloaked_StartsTimer_DefersRevealedNotification_AndSkipsSecondShowOnReady()
    {
        ShellHarness.OnSta(() =>
        {
            ShellHarness.UseShellResources();
            var (container, published) = BuildWithEventSpy();
            using var _ = container;
            var window = container.Resolve<ShellWindow>();
            var ready = container.Resolve<AppReadyCoordinator>();
            var cloakCalls = new List<bool>();
            var showCount = 0;
            var activateCount = 0;
            var visible = false;

            window.CloakWindow = (_, cloaked) =>
            {
                cloakCalls.Add(cloaked);
                return false;
            };
            window.ShowWindowOverride = () =>
            {
                visible = true;
                showCount++;
            };
            window.IsWindowVisibleOverride = () => visible;
            window.ActivateWindowOverride = () => activateCount++;
            window.RevealInterval = TimeSpan.FromSeconds(30);

            try
            {
                window.Preload();

                cloakCalls.ShouldBe([true]);
                showCount.ShouldBe(1);
                activateCount.ShouldBe(0);
                window.IsRevealed.ShouldBeFalse();
                window.IsRevealTimerEnabled.ShouldBeTrue();
                published.ShouldNotContain(e => e.Contains(IpcNames.Events.AppRevealed, StringComparison.Ordinal));

                ready.SignalReady();
                ShellHarness.PumpUntil(() => window.IsRevealed);

                window.IsRevealed.ShouldBeTrue();
                window.IsRevealTimerEnabled.ShouldBeFalse();
                showCount.ShouldBe(1);
                activateCount.ShouldBe(0);
                cloakCalls.ShouldBe([true]);
                published.Count(e => e.Contains(IpcNames.Events.AppRevealed, StringComparison.Ordinal)).ShouldBe(1);
            }
            finally
            {
                window.Close();
            }

            return true;
        });
    }

    [Fact]
    public void Preload_WhenNotCloakedAndPageNeverLoads_TimeoutShowsFailurePanelAndNotifiesRevealed()
    {
        ShellHarness.OnSta(() =>
        {
            ShellHarness.UseShellResources();
            var (container, published) = BuildWithEventSpy();
            using var _ = container;
            var window = container.Resolve<ShellWindow>();
            var viewModel = container.Resolve<ShellViewModel>();
            var showCount = 0;
            var visible = false;

            window.CloakWindow = (_, _) => false;
            window.ShowWindowOverride = () =>
            {
                visible = true;
                showCount++;
            };
            window.IsWindowVisibleOverride = () => visible;
            window.ActivateWindowOverride = () => { };
            window.RevealInterval = TimeSpan.FromMilliseconds(30);

            try
            {
                window.Preload();
                ShellHarness.PumpUntil(() => window.IsRevealed);

                window.IsRevealed.ShouldBeTrue();
                window.IsRevealTimerEnabled.ShouldBeFalse();
                viewModel.WebViewFailed.ShouldBeTrue();
                showCount.ShouldBe(1);
                published.Count(e => e.Contains(IpcNames.Events.AppRevealed, StringComparison.Ordinal)).ShouldBe(1);
            }
            finally
            {
                window.Close();
            }

            return true;
        });
    }

    [Fact]
    public void Preload_WhenCloaked_UncloaksShowsAndNotifiesRevealedOnceReady()
    {
        ShellHarness.OnSta(() =>
        {
            ShellHarness.UseShellResources();
            var (container, published) = BuildWithEventSpy();
            using var _ = container;
            var window = container.Resolve<ShellWindow>();
            var ready = container.Resolve<AppReadyCoordinator>();
            var cloakCalls = new List<bool>();
            var showCount = 0;
            var activateCount = 0;
            var visible = false;

            window.CloakWindow = (_, cloaked) =>
            {
                cloakCalls.Add(cloaked);
                return true;
            };
            window.ShowWindowOverride = () =>
            {
                visible = true;
                showCount++;
            };
            window.IsWindowVisibleOverride = () => visible;
            window.ActivateWindowOverride = () => activateCount++;
            window.RevealInterval = TimeSpan.FromSeconds(30);

            try
            {
                window.Preload();

                cloakCalls.ShouldBe([true]);
                showCount.ShouldBe(1);
                window.ShowInTaskbar.ShouldBeFalse();
                window.ShowActivated.ShouldBeFalse();
                window.IsRevealed.ShouldBeFalse();
                window.IsRevealTimerEnabled.ShouldBeTrue();
                published.ShouldNotContain(e => e.Contains(IpcNames.Events.AppRevealed, StringComparison.Ordinal));

                ready.SignalReady();
                ShellHarness.PumpUntil(() => window.IsRevealed);

                cloakCalls.ShouldBe([true, false]);
                showCount.ShouldBe(2);
                activateCount.ShouldBe(1);
                window.ShowInTaskbar.ShouldBeTrue();
                window.ShowActivated.ShouldBeTrue();
                window.IsRevealTimerEnabled.ShouldBeFalse();
                published.Count(e => e.Contains(IpcNames.Events.AppRevealed, StringComparison.Ordinal)).ShouldBe(1);
            }
            finally
            {
                window.Close();
            }

            return true;
        });
    }
}
