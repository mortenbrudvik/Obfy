using System.IO;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;

namespace Obfy.UI.AutomationTests;

/// <summary>
/// Base class for UI automation tests providing app launch and window access.
/// </summary>
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

    private static string GetAppPath()
    {
        // Path to built UI executable relative to test assembly
        var solutionDir = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var appPath = Path.Combine(solutionDir,
            "Src", "Obfy.UI", "bin", "Debug", "net10.0-windows10.0.26100", "ObfyUI.exe");

        if (!File.Exists(appPath))
        {
            throw new FileNotFoundException(
                $"Obfy.UI.exe not found. Please build the UI project first.\nExpected path: {appPath}");
        }

        return appPath;
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
