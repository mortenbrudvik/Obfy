using System.Windows;

namespace WpfApp;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }

    public string SmokeTitle
    {
        get
        {
            if (DataContext is MainViewModel vm)
                return vm.Title;
            return DataContext?.GetType().GetProperty("Title")?.GetValue(DataContext) as string ?? string.Empty;
        }
    }
}
