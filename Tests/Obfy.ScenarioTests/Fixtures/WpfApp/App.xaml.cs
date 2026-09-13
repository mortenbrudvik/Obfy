using System.Windows;

namespace WpfApp;

public partial class App : Application
{
    private void OnStartup(object sender, StartupEventArgs e)
    {
        var window = new MainWindow();
        if (e.Args.Any(a => string.Equals(a, "--smoke", StringComparison.OrdinalIgnoreCase)))
        {
            window.Measure(new Size(320, 200));
            window.Arrange(new Rect(0, 0, 320, 200));
            window.UpdateLayout();
            var title = window.SmokeTitle;
            Console.WriteLine($"SMOKE:{title}");
            Shutdown(title == "Hello from binding" ? 0 : 2);
            return;
        }

        MainWindow = window;
        window.Show();
    }
}
