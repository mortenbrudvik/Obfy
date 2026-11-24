using Obfy.App.Tests.Common;
using Obfy.Core.Services.Solution;
using Shouldly;

namespace Obfy.Tests.Solution;

public class XamlTypeCollectorTests
{
    [Fact]
    public void CollectFromXaml_ExtractsClassAndSubclass_From2006And2009XamlNamespaces()
    {
        const string xaml2006 = """
            <Window x:Class="MyApp.Views.MainWindow"
                    x:Subclass="MyApp.Views.MainWindowBase"
                    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" />
            """;

        const string xaml2009 = """
            <Page xaml:Class="MyApp.Pages.SettingsPage"
                  xaml:Subclass="MyApp.Pages.SettingsBase"
                  xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
                  xmlns:xaml="http://schemas.microsoft.com/winfx/2009/xaml" />
            """;

        XamlTypeCollector.CollectFromXaml(xaml2006).ShouldBe(
        [
            "MyApp.Views.MainWindow",
            "MyApp.Views.MainWindowBase"
        ]);

        XamlTypeCollector.CollectFromXaml(xaml2009).ShouldBe(
        [
            "MyApp.Pages.SettingsBase",
            "MyApp.Pages.SettingsPage"
        ]);
    }

    [Fact]
    public void CollectFromXaml_ExtractsClrNamespaceElements_AndPropertyElementSyntax()
    {
        const string xaml = """
            <Window x:Class="App.ShellWindow"
                    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    xmlns:ctrl="clr-namespace:App.Controls"
                    xmlns:ext="clr-namespace:Shared.Ui;assembly=Shared.Ui">
                <Grid>
                    <ctrl:NavBar ctrl:DragBehavior.Enabled="True">
                        <ctrl:NavBar.Header>
                            <ext:Badge />
                        </ctrl:NavBar.Header>
                    </ctrl:NavBar>
                    <DefaultRoot xmlns="clr-namespace:App.Root" />
                </Grid>
            </Window>
            """;

        var types = XamlTypeCollector.CollectFromXaml(xaml);

        types.ShouldBe(
        [
            "App.Controls.DragBehavior",
            "App.Controls.NavBar",
            "App.Root.DefaultRoot",
            "App.ShellWindow",
            "Shared.Ui.Badge"
        ]);
    }

    [Fact]
    public void CollectFromXaml_ExtractsTypeStaticTargetTypeAndDataType_DeduplicatedAndSorted()
    {
        const string xaml = """
            <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                xmlns:local="clr-namespace:App.Widgets"
                                xmlns:vm="clr-namespace:App.ViewModels;assembly=App">
                <Style TargetType="Button" />
                <Style TargetType="local:FancyButton" />
                <Style TargetType="{x:Type local:FancyButton}" />
                <DataTemplate DataType="vm:ItemViewModel" />
                <DataTemplate x:DataType="vm:DetailViewModel" />
                <Style TargetType="{x:Type TypeName=local:ThemedPanel}">
                    <Setter Property="Tag" Value="{x:Static local:ThemeKeys.Accent}" />
                    <Setter Property="ToolTip" Value="{Binding Source={x:Static Member=local:Tooltips.Default}, ConverterParameter={x:Type local:TitleConverter}}" />
                </Style>
            </ResourceDictionary>
            """;

        var types = XamlTypeCollector.CollectFromXaml(xaml);

        types.ShouldBe(
        [
            "App.ViewModels.DetailViewModel",
            "App.ViewModels.ItemViewModel",
            "App.Widgets.FancyButton",
            "App.Widgets.ThemeKeys",
            "App.Widgets.ThemedPanel",
            "App.Widgets.TitleConverter",
            "App.Widgets.Tooltips"
        ]);
    }

    [Fact]
    public void CollectFromXaml_IgnoresCommentsEscapedExtensionsMalformedXmlAndDtds()
    {
        const string withCommentsAndEscaped = """
            <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    xmlns:local="clr-namespace:App.Ui">
                <!-- <local:CommentedControl /> -->
                <TextBlock Text="{}{x:Type local:EscapedLiteral}" />
                <local:RealControl />
            </Window>
            """;

        XamlTypeCollector.CollectFromXaml(withCommentsAndEscaped).ShouldBe(["App.Ui.RealControl"]);

        const string malformed = "<Window x:Class=\"App.Broken\"><Unclosed>";
        XamlTypeCollector.CollectFromXaml(malformed).ShouldBeEmpty();

        const string withDtd = """
            <!DOCTYPE Window [
              <!ENTITY name "App.Evil">
            ]>
            <Window x:Class="App.MainWindow"
                    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" />
            """;
        XamlTypeCollector.CollectFromXaml(withDtd).ShouldBeEmpty();
    }

    [Fact]
    public void CollectFromProjectDirectory_SkipsBuildToolDirectories_AndIgnoresMalformedFiles()
    {
        using var dir = new TempDir();

        dir.Write("App.xaml", """
            <Application x:Class="MyApp.App"
                         xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" />
            """);
        dir.Write(Path.Combine("Views", "MainWindow.xaml"), """
            <Window x:Class="MyApp.Views.MainWindow"
                    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" />
            """);
        dir.Write(Path.Combine("Views", "Broken.xaml"), "<Window><not-closed>");

        foreach (var skipped in new[] { "bin", "BIN", "obj", "Obj", ".git", "node_modules", ".vs", "packages" })
        {
            dir.Write(Path.Combine(skipped, "Ignored.xaml"), $$"""
                <Window x:Class="MyApp.Skipped_{{skipped.Replace(".", "")}}"
                        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" />
                """);
        }

        var collected = XamlTypeCollector.CollectFromProjectDirectory(dir.Root);

        collected.ShouldBe(["MyApp.App", "MyApp.Views.MainWindow"]);
    }

    [Fact]
    public void CollectFromXaml_ExtractsQuotedMarkupExtensionArguments()
    {
        const string xaml = """
            <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                xmlns:local="clr-namespace:App.Widgets">
                <Style TargetType="{x:Type 'local:SingleQuoted'}" />
                <Style TargetType="{x:Type TypeName='local:NamedSingleQuoted'}" />
                <Style TargetType='{x:Type "local:DoubleQuoted"}' />
                <Style TargetType='{x:Type TypeName="local:NamedDoubleQuoted"}' />
                <Style TargetType="Control">
                    <Setter Property="Tag" Value="{x:Static 'local:QuotedStatic.Accent'}" />
                    <Setter Property="Content" Value="{x:Static Member='local:NamedQuotedStatic.Default'}" />
                </Style>
            </ResourceDictionary>
            """;

        XamlTypeCollector.CollectFromXaml(xaml).ShouldBe(
        [
            "App.Widgets.DoubleQuoted",
            "App.Widgets.NamedDoubleQuoted",
            "App.Widgets.NamedQuotedStatic",
            "App.Widgets.NamedSingleQuoted",
            "App.Widgets.QuotedStatic",
            "App.Widgets.SingleQuoted"
        ]);
    }

    [Fact]
    public void CollectFromXaml_HandlesRepeatedUnclosedMarkupExtensionsGracefully()
    {
        var unclosed = string.Concat(Enumerable.Repeat("{x:Type ", 5000));
        var xaml = $"""
            <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    xmlns:local="clr-namespace:App.Ui">
                <TextBlock Tag="{unclosed}" />
                <local:AfterUnclosed />
            </Window>
            """;

        XamlTypeCollector.CollectFromXaml(xaml).ShouldBe(["App.Ui.AfterUnclosed"]);
    }

    [Fact]
    public void CollectFromProjectDirectory_RespectsMaxDirectoryDepth()
    {
        using var dir = new TempDir();

        var atLimitDir = Path.Combine([.. Enumerable.Repeat("x", XamlTypeCollector.MaxDirectoryDepth)]);
        dir.Write(Path.Combine(atLimitDir, "AtLimit.xaml"), """
            <Window x:Class="MyApp.AtLimit"
                    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" />
            """);

        var tooDeepDir = Path.Combine(atLimitDir, "x");
        dir.Write(Path.Combine(tooDeepDir, "TooDeep.xaml"), """
            <Window x:Class="MyApp.TooDeep"
                    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" />
            """);

        var collected = XamlTypeCollector.CollectFromProjectDirectory(dir.Root);

        collected.ShouldBe(["MyApp.AtLimit"]);
    }

    [JunctionFact]
    public void CollectFromProjectDirectory_SkipsDirectoryReparsePoints()
    {
        using var projectDir = new TempDir();
        using var externalDir = new TempDir();

        projectDir.Write("Root.xaml", """
            <Window x:Class="MyApp.RootWindow"
                    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" />
            """);
        externalDir.Write("Linked.xaml", """
            <Window x:Class="MyApp.FromJunction"
                    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" />
            """);

        var externalJunction = Path.Combine(projectDir.Root, "LinkedViews");
        var cyclicJunction = Path.Combine(projectDir.Root, "Loop");
        try
        {
            Junctions.TryCreate(externalJunction, externalDir.Root, out var extError).ShouldBeTrue(extError);
            Junctions.TryCreate(cyclicJunction, projectDir.Root, out var loopError).ShouldBeTrue(loopError);

            var collected = XamlTypeCollector.CollectFromProjectDirectory(projectDir.Root);

            collected.ShouldBe(["MyApp.RootWindow"]);
        }
        finally
        {
            if (Directory.Exists(cyclicJunction))
                Directory.Delete(cyclicJunction);
            if (Directory.Exists(externalJunction))
                Directory.Delete(externalJunction);
        }
    }
}
