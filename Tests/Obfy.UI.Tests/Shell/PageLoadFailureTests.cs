using System.IO;
using Autofac;
using Microsoft.Web.WebView2.Core;
using Obfy.UI.ViewModels;
using Obfy.WebView;
using Shouldly;

namespace Obfy.UI.Tests.Shell;

/// <summary>
/// A page that cannot load used to leave a blank window: in Release the page folder was mapped without checking that
/// index.html is in it, a failed or 404 navigation was only logged, and the reveal timeout only showed the blank window.
/// </summary>
public class PageLoadFailureTests
{
    [Theory]
    [InlineData(true, CoreWebView2WebErrorStatus.Unknown, 200, false)]
    [InlineData(true, CoreWebView2WebErrorStatus.Unknown, 304, false)]
    [InlineData(true, CoreWebView2WebErrorStatus.Unknown, 0, false)]
    [InlineData(true, CoreWebView2WebErrorStatus.Unknown, 404, true)]
    [InlineData(true, CoreWebView2WebErrorStatus.Unknown, 500, true)]
    [InlineData(false, CoreWebView2WebErrorStatus.ConnectionAborted, 0, true)]
    [InlineData(false, CoreWebView2WebErrorStatus.CannotConnect, 0, true)]
    [InlineData(false, CoreWebView2WebErrorStatus.ServerUnreachable, 0, true)]
    // A cancelled navigation is not a failed page: NavigationStarting cancels a drop of a document or a link out of the
    // app, and a newer navigation supersedes an older one (a full reload). Neither may bring up the failure panel.
    [InlineData(false, CoreWebView2WebErrorStatus.OperationCanceled, 0, false)]
    public void IsLoadFailure_SeparatesAPageThatDidNotLoadFromOneThatWasCancelled(
        bool isSuccess, CoreWebView2WebErrorStatus status, int httpStatus, bool expected) =>
        WebViewHandler.IsLoadFailure(isSuccess, status, httpStatus).ShouldBe(expected);

    [Fact]
    public void EnsureWebRoot_WithTheEntryPage_Passes()
    {
        var folder = Path.Combine(Path.GetTempPath(), "obfy-webroot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(Path.Combine(folder, "index.html"), "<html></html>");

            Should.NotThrow(() => WebViewHandler.EnsureWebRoot(folder));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void EnsureWebRoot_WithoutTheEntryPage_Throws_AndTheMessageNamesNoPath()
    {
        var folder = Path.Combine(Path.GetTempPath(), "obfy-webroot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var ex = Should.Throw<FileNotFoundException>(() => WebViewHandler.EnsureWebRoot(folder));

            // It reaches the log with the exception; nothing the machine or the user chose may be in it.
            ex.Message.ShouldNotContain(folder);
            ex.Message.ShouldNotContain(Path.GetTempPath());
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void EnsureWebRoot_WithAFolderThatIsNotThere_Throws()
    {
        var folder = Path.Combine(Path.GetTempPath(), "obfy-webroot-missing-" + Guid.NewGuid().ToString("N"));

        Should.Throw<FileNotFoundException>(() => WebViewHandler.EnsureWebRoot(folder));
    }

    private static ShellViewModel Shell(IContainer container) => container.Resolve<ShellViewModel>();

    [Fact]
    public void ANavigationThatFailed_ShowsTheFailurePanel()
    {
        ShellHarness.OnSta(() =>
        {
            using var container = ShellHarness.BuildContainer();
            var shell = Shell(container);

            shell.OnLoadFailed();

            shell.WebViewFailed.ShouldBeTrue();
            return true;
        });
    }

    [Fact]
    public void TheRevealTimeout_WithAPageThatNeverLoaded_ShowsTheFailurePanel()
    {
        ShellHarness.OnSta(() =>
        {
            using var container = ShellHarness.BuildContainer();
            var shell = Shell(container);

            shell.OnRevealTimeout();

            shell.WebViewFailed.ShouldBeTrue();
            return true;
        });
    }

    [Fact]
    public void TheRevealTimeout_WithALoadedPage_ThatIsOnlyLateToSayReady_ShowsNothing()
    {
        // The page says app.ready after it has loaded; a page that is slow (or, as it once was, never) to say so is no failure.
        ShellHarness.OnSta(() =>
        {
            using var container = ShellHarness.BuildContainer();
            var shell = Shell(container);

            shell.OnPageLoaded();
            shell.OnRevealTimeout();

            shell.PageLoaded.ShouldBeTrue();
            shell.WebViewFailed.ShouldBeFalse();
            return true;
        });
    }

    [Fact]
    public void TheRevealTimeout_AfterThePageSaidItIsReady_ShowsNothing()
    {
        ShellHarness.OnSta(() =>
        {
            using var container = ShellHarness.BuildContainer();
            var shell = Shell(container);
            container.Resolve<Obfy.App.Features.App.AppReadyCoordinator>().SignalReady();

            shell.OnRevealTimeout();

            shell.WebViewFailed.ShouldBeFalse();
            return true;
        });
    }

    [Fact]
    public void APanelFromASlowStart_GoesAwayWhenThePageLoadsAfterAll()
    {
        // A cold WebView2 start can take longer than the reveal timeout; the page then loads and the panel must not stay.
        ShellHarness.OnSta(() =>
        {
            using var container = ShellHarness.BuildContainer();
            var shell = Shell(container);
            shell.OnRevealTimeout();
            shell.WebViewFailed.ShouldBeTrue();

            shell.OnPageLoaded();

            shell.WebViewFailed.ShouldBeFalse();
            return true;
        });
    }

    [Fact]
    public void AFailureForGood_IsNotTakenBackByAPageLoad()
    {
        ShellHarness.OnSta(() =>
        {
            using var container = ShellHarness.BuildContainer();
            var shell = Shell(container);

            shell.OnUnrecoverable();
            shell.OnPageLoaded();

            shell.WebViewFailed.ShouldBeTrue();
            return true;
        });
    }

    [Fact]
    public void AnInitializationFailure_IsNotTakenBackByAPageLoad()
    {
        ShellHarness.OnSta(() =>
        {
            using var container = ShellHarness.BuildContainer();
            var shell = Shell(container);

            shell.OnInitializeFailed(new InvalidOperationException("boom"));
            shell.OnPageLoaded();

            shell.WebViewFailed.ShouldBeTrue();
            return true;
        });
    }
}
