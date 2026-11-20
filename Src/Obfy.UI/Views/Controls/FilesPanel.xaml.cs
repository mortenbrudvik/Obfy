using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Obfy.UI.Models;
using Obfy.UI.ViewModels;

namespace Obfy.UI.Views.Controls;

/// <summary>
/// Interaction logic for FilesPanel.xaml
/// </summary>
public partial class FilesPanel : UserControl
{
    public FilesPanel()
    {
        InitializeComponent();
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop)
            && e.Data.GetData(DataFormats.FileDrop) is string[] paths
            && FilesViewModel.CanAcceptDrop(paths))
        {
            e.Effects = DragDropEffects.Copy;
            DropOverlay.Visibility = Visibility.Visible;
        }
        else
        {
            e.Effects = DragDropEffects.None;
            DropOverlay.Visibility = Visibility.Collapsed;
        }

        e.Handled = true;
    }

    private void OnDragLeave(object sender, DragEventArgs e)
    {
        if (sender is not FrameworkElement element)
            return;

        var point = e.GetPosition(element);
        if (point.X < 0 || point.Y < 0 || point.X >= element.ActualWidth || point.Y >= element.ActualHeight)
            DropOverlay.Visibility = Visibility.Collapsed;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;

        if (e.Data.GetDataPresent(DataFormats.FileDrop)
            && e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            if (Window.GetWindow(this)?.DataContext is MainViewModel host)
                host.HandleDrop(files);
            else if (DataContext is FilesViewModel viewModel)
                viewModel.HandleFileDrop(files);
        }

        e.Handled = true;
    }

    private void OnPreviewRightClick(object sender, MouseButtonEventArgs e)
    {
        var item = ItemsControl.ContainerFromElement(FilesList, e.OriginalSource as DependencyObject) as ListViewItem;
        if (item is null)
            return;

        if (!item.IsSelected)
            FilesList.SelectedItem = item.DataContext;
    }

    private void OnFilesKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Delete || DataContext is not FilesViewModel viewModel)
            return;

        viewModel.RemoveFiles(FilesList.SelectedItems.OfType<AssemblyFile>());
        e.Handled = true;
    }

    private void OnPreviewOutputClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: AssemblyFile file })
            return;

        if (Window.GetWindow(this)?.DataContext is MainViewModel host)
            host.PreviewFileCommand.Execute(file);
    }
}
