using Autofac;
using Logging.Core.Configuration;
using Obfy.UI.ViewModels;
using Shouldly;

namespace Obfy.UI.Tests.Shell;

/// <summary>
/// The failure panel points the user at the log. When the log folder could not be created (reported on Console.Error, which
/// a WPF app does not have) it must not claim there is a log to read or a folder to open.
/// </summary>
public class FailurePanelLogTests
{
    [Fact]
    public void WithALogFile_ThePanelSaysWhereItIs_AndOffersTheFolder()
    {
        ShellHarness.OnSta(() =>
        {
            using var container = ShellHarness.BuildContainer(b => b.RegisterInstance(new LogFileStatus(true, null)));
            var shell = container.Resolve<ShellViewModel>();

            shell.LogsAvailable.ShouldBeTrue();
            shell.LogsNote.ShouldContain(shell.LogsDirectory);
            shell.OpenLogFolderCommand.CanExecute(null).ShouldBeTrue();
            return true;
        });
    }

    [Fact]
    public void WithoutALogFile_ThePanelSaysSo_AndDoesNotOfferAFolderThatIsNotThere()
    {
        ShellHarness.OnSta(() =>
        {
            using var container = ShellHarness.BuildContainer(b => b.RegisterInstance(new LogFileStatus(false, "IOException")));
            var shell = container.Resolve<ShellViewModel>();

            shell.LogsAvailable.ShouldBeFalse();
            shell.LogsNote.ShouldNotContain(shell.LogsDirectory);
            shell.LogsNote.ShouldContain("could not");
            shell.OpenLogFolderCommand.CanExecute(null).ShouldBeFalse();
            return true;
        });
    }

    [Fact]
    public void TheContainer_KnowsWhetherTheLogFileCanBeWritten()
    {
        ShellHarness.OnSta(() =>
        {
            using var container = ShellHarness.BuildContainer();

            container.Resolve<LogFileStatus>().ShouldNotBeNull();
            return true;
        });
    }
}
