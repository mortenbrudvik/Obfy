using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Obfy.UI.Models;
using Obfy.UI.ViewModels;

namespace Obfy.UI.Views.Controls;

/// <summary>
/// Interaction logic for ResultsPanel.xaml
/// </summary>
public partial class ResultsPanel : UserControl
{
    public ResultsPanel()
    {
        InitializeComponent();
    }

    private void OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is ResultsViewModel viewModel)
            viewModel.SelectedNode = e.NewValue as SymbolTreeNode;
    }

    private void OnTreeKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.C || Keyboard.Modifiers != ModifierKeys.Control)
            return;

        if (DataContext is ResultsViewModel viewModel)
            viewModel.CopySymbolCommand.Execute(viewModel.SelectedNode);

        e.Handled = true;
    }
}
