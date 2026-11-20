using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using Shouldly;

namespace Obfy.UI.AutomationTests;

/// <summary>
/// The one FlaUI test left: the native window and its title bar, which Playwright cannot see. Everything inside the
/// web view is covered by <see cref="WebUiTests"/>. Category=UI, so default <c>dotnet test</c> skips it.
/// </summary>
[Trait("Category", "UI")]
public class FlaUiSmokeTests : IDisposable
{
    private readonly UIA3Automation _automation = new();
    private readonly Application _app;

    public FlaUiSmokeTests()
    {
        _app = UiLaunch.Launch(UiExecutableLocator.ResolveFromTestContext(AppContext.BaseDirectory));
    }

    [Fact]
    public void Launch_ShowsTheWindowWithItsTitleBar()
    {
        Window window = _app.GetMainWindow(_automation, TimeSpan.FromSeconds(30))
            ?? throw new InvalidOperationException("The Obfy window did not appear.");

        window.IsAvailable.ShouldBeTrue();
        window.IsOffscreen.ShouldBeFalse();
        window.Title.ShouldBe("Obfy");
        window.BoundingRectangle.Width.ShouldBeGreaterThan(0);
        window.BoundingRectangle.Height.ShouldBeGreaterThan(0);

        // The title bar is the ui:TitleBar the window extends its content into; it exposes its title as text.
        window.FindFirstDescendant(cf => cf.ByName("Obfy").And(cf.ByControlType(FlaUI.Core.Definitions.ControlType.Text)))
            .ShouldNotBeNull("the title bar's \"Obfy\" caption should be in the window");
    }

    public void Dispose()
    {
        try
        {
            _app.Close();
            if (!_app.HasExited)
                _app.Kill();
        }
        catch (Exception)
        {
            // The process is already gone.
        }

        _automation.Dispose();
        GC.SuppressFinalize(this);
    }
}
