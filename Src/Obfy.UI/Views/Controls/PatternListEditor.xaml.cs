using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Obfy.UI.Views.Controls;

public partial class PatternListEditor : UserControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(PatternListEditor));

    public static readonly DependencyProperty ItemsProperty =
        DependencyProperty.Register(nameof(Items), typeof(IEnumerable), typeof(PatternListEditor));

    public static readonly DependencyProperty DraftProperty =
        DependencyProperty.Register(nameof(Draft), typeof(string), typeof(PatternListEditor),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty AddCommandProperty =
        DependencyProperty.Register(nameof(AddCommand), typeof(ICommand), typeof(PatternListEditor));

    public static readonly DependencyProperty RemoveCommandProperty =
        DependencyProperty.Register(nameof(RemoveCommand), typeof(ICommand), typeof(PatternListEditor));

    public static readonly DependencyProperty PlaceholderProperty =
        DependencyProperty.Register(nameof(Placeholder), typeof(string), typeof(PatternListEditor));

    public static readonly DependencyProperty DraftAutomationNameProperty =
        DependencyProperty.Register(nameof(DraftAutomationName), typeof(string), typeof(PatternListEditor));

    public static readonly DependencyProperty AddAutomationNameProperty =
        DependencyProperty.Register(nameof(AddAutomationName), typeof(string), typeof(PatternListEditor));

    public static readonly DependencyProperty RemoveAutomationNameProperty =
        DependencyProperty.Register(nameof(RemoveAutomationName), typeof(string), typeof(PatternListEditor));

    public PatternListEditor()
    {
        InitializeComponent();
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public IEnumerable Items
    {
        get => (IEnumerable)GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public string Draft
    {
        get => (string)GetValue(DraftProperty);
        set => SetValue(DraftProperty, value);
    }

    public ICommand AddCommand
    {
        get => (ICommand)GetValue(AddCommandProperty);
        set => SetValue(AddCommandProperty, value);
    }

    public ICommand RemoveCommand
    {
        get => (ICommand)GetValue(RemoveCommandProperty);
        set => SetValue(RemoveCommandProperty, value);
    }

    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    public string DraftAutomationName
    {
        get => (string)GetValue(DraftAutomationNameProperty);
        set => SetValue(DraftAutomationNameProperty, value);
    }

    public string AddAutomationName
    {
        get => (string)GetValue(AddAutomationNameProperty);
        set => SetValue(AddAutomationNameProperty, value);
    }

    public string RemoveAutomationName
    {
        get => (string)GetValue(RemoveAutomationNameProperty);
        set => SetValue(RemoveAutomationNameProperty, value);
    }
}
