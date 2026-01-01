using System.Windows;
using System.Windows.Controls;
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
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            DropOverlay.Visibility = Visibility.Visible;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void OnDragLeave(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;

        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files != null && DataContext is FilesViewModel viewModel)
            {
                viewModel.HandleFileDrop(files);
            }
        }
    }
}
