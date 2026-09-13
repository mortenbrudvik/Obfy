using System.IO;
using System.Xml.Linq;
using Shouldly;

namespace Obfy.UI.Tests.Views;

/// <summary>
/// WPF-UI's TextBlock default Foreground is black. Any view TextBlock without an
/// explicit theme brush is unreadable on the dark theme, especially inside item templates.
/// </summary>
public class ThemeForegroundXamlTests
{
    private static readonly XNamespace Presentation =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Ui = "http://schemas.lepo.co/wpfui/2022/xaml";

    [Fact]
    public void Views_DoNotUseStockListView()
    {
        var stock = GetViewXamlFiles()
            .SelectMany(path => XDocument.Load(path).Descendants(Presentation + "ListView")
                .Select(el => $"{Path.GetFileName(path)} ListView"))
            .ToList();

        stock.ShouldBeEmpty();
    }

    [Fact]
    public void ViewTextBlocks_DeclareThemeForeground()
    {
        var missing = new List<string>();

        foreach (var path in GetViewXamlFiles())
        {
            var xml = XDocument.Load(path);
            foreach (var el in xml.Descendants().Where(IsTextBlock))
            {
                if (HasForeground(el))
                {
                    continue;
                }

                missing.Add($"{Path.GetFileName(path)}: {Describe(el)}");
            }
        }

        missing.ShouldBeEmpty();
    }

    private static bool IsTextBlock(XElement el)
        => el.Name == Presentation + "TextBlock" || el.Name == Ui + "TextBlock";

    private static bool HasForeground(XElement el)
    {
        if (el.Attribute("Foreground") is not null)
        {
            return true;
        }

        return el.Descendants().Any(child =>
            child.Name.LocalName == "Setter"
            && (string?)child.Attribute("Property") == "Foreground");
    }

    private static string Describe(XElement el)
    {
        var text = (string?)el.Attribute("Text");
        if (!string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        var id = (string?)el.Attribute("AutomationProperties.AutomationId");
        return string.IsNullOrWhiteSpace(id) ? el.Name.LocalName : id;
    }

    private static IEnumerable<string> GetViewXamlFiles()
    {
        var views = FindViewsDirectory();
        return Directory.GetFiles(views, "*.xaml", SearchOption.AllDirectories)
            .Where(path => !path.EndsWith(".xaml.cs", StringComparison.OrdinalIgnoreCase));
    }

    private static string FindViewsDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "Src", "Obfy.UI", "Views");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Src/Obfy.UI/Views not found from " + AppContext.BaseDirectory);
    }
}
