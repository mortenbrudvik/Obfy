using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;

namespace Obfy.UI.AutomationTests;

/// <summary>
/// Base class for UI automation tests providing app launch and window access.
/// FlaUI tests are Category=UI: skipped by default <c>dotnet test</c> / CI.
/// </summary>
[Trait("Category", "UI")]
public abstract class TestBase : IDisposable
{
    protected Application App { get; private set; } = null!;
    protected UIA3Automation Automation { get; private set; } = null!;
    protected Window MainWindow { get; private set; } = null!;

    private static readonly string AppPath = UiExecutableLocator.ResolveFromTestContext(AppContext.BaseDirectory);

    protected TestBase()
    {
        Automation = new UIA3Automation();
        App = Application.Launch(AppPath);
        MainWindow = App.GetMainWindow(Automation, TimeSpan.FromSeconds(10))
            ?? throw new InvalidOperationException("Obfy main window did not appear.");
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
