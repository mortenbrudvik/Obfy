using System.Collections.Specialized;
using System.Windows.Controls;
using Obfy.UI.ViewModels;

namespace Obfy.UI.Views.Controls;

/// <summary>
/// Interaction logic for OutputPanel.xaml
/// </summary>
public partial class OutputPanel : UserControl
{
    public OutputPanel()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Unloaded += OnUnloaded;
    }

    private void OnUnloaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is OutputViewModel vm)
            vm.Logs.CollectionChanged -= OnLogsCollectionChanged;
    }

    private void OnDataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is OutputViewModel oldVm)
        {
            oldVm.Logs.CollectionChanged -= OnLogsCollectionChanged;
        }

        if (e.NewValue is OutputViewModel newVm)
        {
            newVm.Logs.CollectionChanged += OnLogsCollectionChanged;
        }
    }

    private void OnLogsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (DataContext is OutputViewModel vm && vm.AutoScroll && e.Action == NotifyCollectionChangedAction.Add)
        {
            if (LogListBox.Items.Count > 0)
            {
                LogListBox.ScrollIntoView(LogListBox.Items[^1]);
            }
        }
    }
}
