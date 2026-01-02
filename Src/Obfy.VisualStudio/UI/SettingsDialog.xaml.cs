using System.Windows;

namespace Obfy.VisualStudio.UI;

/// <summary>
/// Interaction logic for SettingsDialog.xaml
/// </summary>
public partial class SettingsDialog : Window
{
    public SettingsDialog(SettingsDialogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
