using System.IO;
using System.Xml.Linq;
using Shouldly;

namespace Obfy.UI.Tests.Views;

public class HelpDialogContentXamlTests
{
    private static readonly XNamespace Ui = "http://schemas.lepo.co/wpfui/2022/xaml";

    [Fact]
    public void TopicList_IsWpfUiListView()
    {
        LoadTopicList().Name.ShouldBe(Ui + "ListView");
    }

    [Fact]
    public void UserControl_IsPinnedAt840By480()
    {
        var root = XDocument.Load(FindXaml()).Root.ShouldNotBeNull();
        root.Attribute("Width")!.Value.ShouldBe("840");
        root.Attribute("Height")!.Value.ShouldBe("480");
        root.Attribute("MinWidth")!.Value.ShouldBe("840");
        root.Attribute("MinHeight")!.Value.ShouldBe("480");
    }

    [Fact]
    public void TopicList_ItemNameBindsToTitle()
    {
        var setter = LoadTopicList()
            .Descendants()
            .Single(e => e.Name.LocalName == "Setter"
                         && (string?)e.Attribute("Property") == "AutomationProperties.Name");
        setter.Attribute("Value")!.Value.ShouldBe("{Binding Title}");
    }

    [Fact]
    public void TopicTitle_UsesThemePrimaryForeground()
    {
        var xaml = XDocument.Load(FindXaml());
        var title = xaml.Descendants(Ui + "TextBlock")
            .Single(e => (string?)e.Attribute("Text") == "{Binding SelectedTopic.Title}");
        title.Attribute("Foreground")!.Value.ShouldBe("{DynamicResource TextFillColorPrimaryBrush}");
    }

    [Fact]
    public void BlocksItemsControl_HasNoItemTemplate_AndHasDataTypeTemplates()
    {
        var items = XDocument.Load(FindXaml())
            .Descendants()
            .Single(e => e.Name.LocalName == "ItemsControl");
        items.Attribute("ItemTemplate").ShouldBeNull();

        var dataTypes = items.Descendants()
            .Where(e => e.Name.LocalName == "DataTemplate")
            .Select(e => (string?)e.Attribute("DataType"))
            .ToList();
        dataTypes.ShouldContain(t => t != null && t.Contains("HelpParagraph"));
        dataTypes.ShouldContain(t => t != null && t.Contains("HelpShortcut"));
        dataTypes.ShouldContain(t => t != null && t.Contains("HelpNamedNote"));
    }

    private static XElement LoadTopicList() =>
        XDocument.Load(FindXaml()).Descendants()
            .Single(e => (string?)e.Attribute("AutomationProperties.AutomationId") == "HelpTopicList");

    private static string FindXaml()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "Src", "Obfy.UI", "Views", "HelpDialogContent.xaml");
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException("HelpDialogContent.xaml not found from " + AppContext.BaseDirectory);
    }
}
