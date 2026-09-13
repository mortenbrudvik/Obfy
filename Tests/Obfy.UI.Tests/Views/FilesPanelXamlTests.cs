using System.IO;
using System.Xml.Linq;
using Shouldly;

namespace Obfy.UI.Tests.Views;

/// <summary>
/// Markup contracts for the input-files list. Stock WPF ListViewItem uses
/// SystemColors.ControlTextBrush (black), which is unreadable on the dark theme.
/// </summary>
public class FilesPanelXamlTests
{
    private static readonly XNamespace Ui = "http://schemas.lepo.co/wpfui/2022/xaml";

    [Fact]
    public void FilesList_UsesWpfUiListView()
    {
        var filesList = LoadFilesListElement();

        filesList.Name.ShouldBe(Ui + "ListView");
    }

    [Fact]
    public void FileName_UsesThemePrimaryForeground()
    {
        var filesList = LoadFilesListElement();
        var fileName = filesList
            .Descendants(Ui + "TextBlock")
            .Single(e => (string?)e.Attribute("Text") == "{Binding FileName}");

        fileName.Attribute("Foreground").ShouldNotBeNull().Value
            .ShouldBe("{DynamicResource TextFillColorPrimaryBrush}");
    }

    private static XElement LoadFilesListElement()
    {
        var xaml = XDocument.Load(FindFilesPanelXaml());
        return xaml.Descendants().Single(e =>
            (string?)e.Attribute("AutomationProperties.AutomationId") == "FilesList");
    }

    private static string FindFilesPanelXaml()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "Src", "Obfy.UI", "Views", "Controls", "FilesPanel.xaml");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException("FilesPanel.xaml not found from " + AppContext.BaseDirectory);
    }
}
