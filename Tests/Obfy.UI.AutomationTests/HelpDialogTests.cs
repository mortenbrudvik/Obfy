using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using Shouldly;

namespace Obfy.UI.AutomationTests;

/// <summary>
/// Help is a WPF-UI ContentDialog overlay, not a separate window.
/// Do not find the dialog by ByName("Help") — that matches the toolbar button.
/// </summary>
[Trait("Category", "UI")]
public class HelpDialogTests : TestBase
{
    [Fact]
    public void HelpButton_OpensInWindowHelpDialog()
    {
        var help = FindById("HelpButton")?.AsButton();
        help.ShouldNotBeNull();
        help.Click();

        WaitForElement("HelpTopicList", TimeSpan.FromSeconds(5))
            .ShouldNotBeNull("Help ContentDialog should open in the main window");
        FindById("HelpArticle").ShouldNotBeNull();

        CloseHelp();
    }

    [Fact]
    public void HelpDialog_Close_DismissesIt()
    {
        FindById("HelpButton")!.AsButton().Click();
        WaitForElement("HelpTopicList", TimeSpan.FromSeconds(5)).ShouldNotBeNull();
        CloseHelp();
        Thread.Sleep(400);
        FindById("HelpTopicList").ShouldBeNull();
    }

    [Fact]
    public void F1_OpensHelpDialog()
    {
        MainWindow.Focus();
        Keyboard.Press(VirtualKeyShort.F1);
        WaitForElement("HelpTopicList", TimeSpan.FromSeconds(5)).ShouldNotBeNull();
        CloseHelp();
    }

    [Fact]
    public void HelpDialog_Escape_DismissesIt()
    {
        FindById("HelpButton")!.AsButton().Click();
        WaitForElement("HelpTopicList", TimeSpan.FromSeconds(5)).ShouldNotBeNull();
        Keyboard.Press(VirtualKeyShort.ESCAPE);
        Thread.Sleep(400);
        FindById("HelpTopicList").ShouldBeNull();
        MainWindow.IsAvailable.ShouldBeTrue("Escape must not close the app");
    }

    private void CloseHelp()
    {
        var close = WaitForName("Close", TimeSpan.FromSeconds(3))?.AsButton();
        close.ShouldNotBeNull();
        close.Click();
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
}
