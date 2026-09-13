using System.IO;
using System.Xml.Linq;
using Shouldly;

namespace Obfy.UI.Tests.Views;

/// <summary>
/// WPF-UI maps disabled buttons to TextFillColorDisabled / ControlFillColorDisabled,
/// which are nearly invisible on the dark theme. App.xaml must override those keys.
/// </summary>
public class DisabledButtonContrastXamlTests
{
    [Theory]
    [InlineData("ButtonForegroundDisabled", "TextFillColorSecondary")]
    [InlineData("ButtonBackgroundDisabled", "ControlFillColorDefault")]
    [InlineData("ButtonBorderBrushDisabled", "ControlStrongStrokeColorDefault")]
    public void AppXaml_OverridesDisabledButtonBrush(string key, string expectedColorResource)
    {
        var xml = XDocument.Load(FindAppXaml());
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var brush = xml.Descendants(presentation + "SolidColorBrush")
            .SingleOrDefault(el => (string?)el.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml")) == key);

        brush.ShouldNotBeNull($"App.xaml must override {key}");
        brush.Attribute("Color").ShouldNotBeNull().Value
            .ShouldContain(expectedColorResource);
    }

    private static string FindAppXaml()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "Src", "Obfy.UI", "App.xaml");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException("App.xaml not found from " + AppContext.BaseDirectory);
    }
}
