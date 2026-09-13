using System.IO;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;

namespace Obfy.UI.AutomationTests;

/// <summary>
/// Base class for UI automation tests providing app launch and window access.
/// Trait Category=UI so default/CI <c>dotnet test</c> can exclude this suite.
/// </summary>
[Trait("Category", "UI")]
public abstract class TestBase : IDisposable
{
    protected Application App { get; private set; } = null!;
    protected UIA3Automation Automation { get; private set; } = null!;
    protected Window MainWindow { get; private set; } = null!;

    private static readonly string AppPath = GetAppPath();

    protected TestBase()
    {
        Automation = new UIA3Automation();
        App = Application.Launch(AppPath);
        MainWindow = App.GetMainWindow(Automation, TimeSpan.FromSeconds(10));
    }

    internal static string GetAppPath()
    {
        var solutionDir = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var tfm = "net10.0-windows10.0.26100";
        var config = AppContext.BaseDirectory.Contains(
            $"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase)
            ? "Release"
            : "Debug";

        foreach (var candidate in new[]
                 {
                     Path.Combine(solutionDir, "Src", "Obfy.UI", "bin", config, tfm, "ObfyUI.exe"),
                     Path.Combine(solutionDir, "Src", "Obfy.UI", "bin", "Release", tfm, "ObfyUI.exe"),
                     Path.Combine(solutionDir, "Src", "Obfy.UI", "bin", "Debug", tfm, "ObfyUI.exe"),
                 })
        {
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException(
            "ObfyUI.exe not found. Build Obfy.UI in the same configuration as the tests " +
            $"(expected under Src/Obfy.UI/bin/{config}/{tfm}/).");
    }

    /// <summary>
    /// Finds an element by its AutomationId.
    /// </summary>
    protected AutomationElement? FindById(string automationId)
    {
        return MainWindow.FindFirstDescendant(cf => cf.ByAutomationId(automationId));
    }

    /// <summary>
    /// Waits for an element to appear with timeout.
    /// </summary>
    protected AutomationElement? WaitForElement(string automationId, TimeSpan timeout)
    {
        var deadline = DateTime.Now + timeout;
        while (DateTime.Now < deadline)
        {
            var element = FindById(automationId);
            if (element != null)
                return element;
            Thread.Sleep(100);
        }
        return null;
    }

    public void Dispose()
    {
        App?.Close();
        Automation?.Dispose();
        GC.SuppressFinalize(this);
    }
}
