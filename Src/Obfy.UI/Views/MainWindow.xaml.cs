using System.Windows;
using Obfy.UI.ViewModels;
using Wpf.Ui;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Obfy.UI.Views;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : FluentWindow
{
    public MainWindow()
    {
        InitializeComponent();
        SystemThemeWatcher.Watch(this);
        Closed += (_, _) => SystemThemeWatcher.UnWatch(this);
    }

    public MainWindow(ISnackbarService snackbarService, IContentDialogService contentDialogService)
        : this()
    {
        snackbarService.SetSnackbarPresenter(SnackbarPresenter);
        contentDialogService.SetDialogHost(RootContentDialog);
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop)
            && e.Data.GetData(DataFormats.FileDrop) is string[] paths
            && FilesViewModel.CanAcceptDrop(paths))
        {
            e.Effects = DragDropEffects.Copy;
            WindowDropOverlay.Visibility = Visibility.Visible;
        }
        else
        {
            e.Effects = DragDropEffects.None;
            WindowDropOverlay.Visibility = Visibility.Collapsed;
        }

        e.Handled = true;
    }

    private void OnDragLeave(object sender, DragEventArgs e)
    {
        if (sender is not FrameworkElement element)
            return;

        var point = e.GetPosition(element);
        if (point.X < 0 || point.Y < 0 || point.X >= element.ActualWidth || point.Y >= element.ActualHeight)
            WindowDropOverlay.Visibility = Visibility.Collapsed;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        WindowDropOverlay.Visibility = Visibility.Collapsed;
        if (e.Data.GetDataPresent(DataFormats.FileDrop)
            && e.Data.GetData(DataFormats.FileDrop) is string[] paths
            && DataContext is MainViewModel viewModel)
        {
            viewModel.HandleDrop(paths);
        }

        e.Handled = true;
    }
}
