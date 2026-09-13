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
