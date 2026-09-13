using System.Windows.Forms;

namespace WinFormsApp;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var form = new MainForm();
        if (args.Any(a => string.Equals(a, "--smoke", StringComparison.OrdinalIgnoreCase)))
        {
            var message = form.Go != null ? "SMOKE:ready" : "SMOKE:miss";
            Console.WriteLine(message);
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "smoke.txt"), message);
            return form.Go != null ? 0 : 2;
        }

        Application.Run(form);
        return 0;
    }
}
