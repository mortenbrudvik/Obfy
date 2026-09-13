using System.IO;
using System.Xml.Linq;
using Shouldly;

namespace Obfy.UI.Tests.Views;

public class MainWindowXamlTests
{
    [Fact]
    public void ObfuscateShortcut_IsCtrlEnter_NotBareEnter()
    {
        var xaml = XDocument.Load(FindMainWindowXaml());
        var bindings = xaml.Descendants().Where(e => e.Name.LocalName == "KeyBinding").ToList();

        bindings.ShouldContain(e =>
            (string?)e.Attribute("Key") == "Enter" &&
            (string?)e.Attribute("Modifiers") == "Control");

        bindings.ShouldNotContain(e =>
            (string?)e.Attribute("Key") == "Enter" &&
            (string?)e.Attribute("Modifiers") != "Control");
    }

    [Fact]
    public void HelpShortcut_IsF1()
    {
        var xaml = XDocument.Load(FindMainWindowXaml());
        var bindings = xaml.Descendants().Where(e => e.Name.LocalName == "KeyBinding").ToList();
        bindings.ShouldContain(e =>
            (string?)e.Attribute("Key") == "F1" &&
            (string?)e.Attribute("Command") == "{Binding ShowHelpCommand}");
    }

    [Fact]
    public void HelpButton_IsLeftOfAbout_AndAboutRemains()
    {
        var xaml = XDocument.Load(FindMainWindowXaml());
        var buttons = xaml.Descendants()
            .Where(e => e.Name.LocalName == "Button")
            .ToList();

        var help = buttons.Single(e =>
            (string?)e.Attribute("AutomationProperties.AutomationId") == "HelpButton");
        var about = buttons.Single(e =>
            (string?)e.Attribute("AutomationProperties.AutomationId") == "AboutButton");

        help.Attribute("Command")!.Value.ShouldBe("{Binding ShowHelpCommand}");
        about.Attribute("Command")!.Value.ShouldBe("{Binding ShowAboutCommand}");

        var helpIndex = buttons.IndexOf(help);
        var aboutIndex = buttons.IndexOf(about);
        helpIndex.ShouldBeLessThan(aboutIndex);
    }

    [Fact]
    public void EscapeBinding_RemainsOnCancel()
    {
        var xaml = XDocument.Load(FindMainWindowXaml());
        xaml.Descendants().ShouldContain(e =>
            e.Name.LocalName == "KeyBinding" &&
            (string?)e.Attribute("Key") == "Escape" &&
            (string?)e.Attribute("Command") == "{Binding CancelCommand}");
    }

    private static string FindMainWindowXaml()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "Src", "Obfy.UI", "Views", "MainWindow.xaml");
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException("MainWindow.xaml not found from " + AppContext.BaseDirectory);
    }
}
