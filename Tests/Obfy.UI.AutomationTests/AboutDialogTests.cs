using FlaUI.Core.AutomationElements;
using Shouldly;

namespace Obfy.UI.AutomationTests;

/// <summary>
/// About is a WPF-UI ContentDialog overlay, not a separate window.
/// </summary>
public class AboutDialogTests : TestBase
{
    [Fact]
    public void AboutButton_OpensAboutDialog()
    {
        var aboutButton = FindById("AboutButton")?.AsButton();
        aboutButton.ShouldNotBeNull();
        aboutButton.Click();

        var title = WaitForName("About Obfy", TimeSpan.FromSeconds(5));
        title.ShouldNotBeNull("About ContentDialog should open in the main window");

        CloseAbout();
    }

    [Fact]
    public void AboutDialog_CanBeClosed()
    {
        var aboutButton = FindById("AboutButton")?.AsButton();
        aboutButton.ShouldNotBeNull();
        aboutButton.Click();

        WaitForName("About Obfy", TimeSpan.FromSeconds(5)).ShouldNotBeNull();
        CloseAbout();
        Thread.Sleep(400);

        MainWindow.FindFirstDescendant(cf => cf.ByName("About Obfy")).ShouldBeNull();
    }

    private AutomationElement? WaitForName(string name, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var element = MainWindow.FindFirstDescendant(cf => cf.ByName(name));
            if (element != null)
                return element;
            Thread.Sleep(100);
        }

        return null;
    }

    private void CloseAbout()
    {
        var close = WaitForName("Close", TimeSpan.FromSeconds(3))?.AsButton();
        close.ShouldNotBeNull();
        close.Click();
    }
}
