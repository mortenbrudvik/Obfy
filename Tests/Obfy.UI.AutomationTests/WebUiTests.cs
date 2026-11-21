using System.IO;
using Microsoft.Playwright;
using Shouldly;
using static Microsoft.Playwright.Assertions;

namespace Obfy.UI.AutomationTests;

/// <summary>
/// End-to-end tests of the web UI inside ObfyUI.exe, driven by Playwright over WebView2's remote-debugging port (see
/// <see cref="ObfyApp"/>). Category=UI: default <c>dotnet test</c> skips them; the Windows CI job runs them against the
/// Release exe, which serves the bundled page (a Debug exe expects the Vite dev server instead).
/// </summary>
[Trait("Category", "UI")]
public class WebUiTests
{
    private static readonly LocatorAssertionsToBeVisibleOptions Visible = new() { Timeout = 30_000 };

    [Fact]
    public async Task FirstLaunch_ShowsTheFilesScreen()
    {
        await using var app = await ObfyApp.StartAsync();

        await Expect(app.Page.GetByRole(AriaRole.Heading, new() { Name = "What do you want to protect?" })).ToBeVisibleAsync(Visible);
        await Expect(app.Page.GetByTestId("nav-files")).ToHaveAttributeAsync("aria-current", "page");
        await Expect(app.Page.GetByTestId("run-button")).ToBeDisabledAsync();
    }

    [Fact]
    public async Task Protection_ToggleATechnique_ReflectsInTheSwitch()
    {
        await using var app = await ObfyApp.StartAsync();

        await app.Page.GetByTestId("nav-protection").ClickAsync();
        var toggle = app.Page.GetByTestId("toggle-stringEncryption");
        await Expect(toggle).ToBeVisibleAsync(Visible);

        // The switch follows the host's answer, so click and wait for the state to follow rather than set it.
        var before = await toggle.IsCheckedAsync();
        await toggle.ClickAsync();
        await Expect(toggle).ToBeCheckedAsync(new() { Checked = !before });

        await toggle.ClickAsync();
        await Expect(toggle).ToBeCheckedAsync(new() { Checked = before });
    }

    [Fact]
    public async Task F1_OpensTheInAppQuickStart()
    {
        await using var app = await ObfyApp.StartAsync();
        await Expect(app.Page.GetByTestId("nav-files")).ToBeVisibleAsync(Visible);

        await app.Page.Keyboard.PressAsync("F1");
        await Expect(app.Page.GetByTestId("guide-page")).ToBeVisibleAsync(Visible);
        await Expect(app.Page.GetByTestId("nav-guide")).ToHaveAttributeAsync("aria-current", "page");
        await Expect(app.Page.GetByRole(AriaRole.Heading, new() { Level = 2, Name = "Getting started" })).ToBeVisibleAsync(Visible);
    }

    [Fact]
    public async Task GuideNav_OpensTheQuickStart_WithALinkToTheFullGuideOnline()
    {
        await using var app = await ObfyApp.StartAsync();

        await app.Page.GetByTestId("nav-guide").ClickAsync();
        await Expect(app.Page.GetByTestId("guide-page")).ToBeVisibleAsync(Visible);

        // The quick-start has two topics. The button is only looked at, never clicked: it would start the machine's browser.
        var topics = app.Page.GetByRole(AriaRole.Navigation, new() { Name = "Guide topics" }).GetByRole(AriaRole.Button);
        await Expect(topics).ToHaveCountAsync(2);
        await Expect(app.Page.GetByRole(AriaRole.Button, new() { Name = "Open the full guide online" })).ToBeVisibleAsync(Visible);
    }

    [Fact]
    public async Task Obfuscate_FromStartupArguments_WritesTheOutputAndShowsResults()
    {
        var work = Path.Combine(Path.GetTempPath(), "obfy-e2e-input-" + Guid.NewGuid().ToString("N"));
        var input = Path.Combine(work, "Sample.dll");
        var output = Path.Combine(work, "out");
        Directory.CreateDirectory(work);
        try
        {
            // A real managed assembly: this test project's own output.
            File.Copy(typeof(WebUiTests).Assembly.Location, input);

            await using var app = await ObfyApp.StartAsync([input, "-o", output]);

            var run = app.Page.GetByTestId("run-button");
            await Expect(run).ToBeEnabledAsync(new() { Timeout = 30_000 });
            await run.ClickAsync();

            // A clean run opens the results by itself.
            await Expect(app.Page.GetByTestId("results-finished")).ToBeVisibleAsync(new() { Timeout = 180_000 });
            await Expect(app.Page.GetByTestId("nav-results")).ToHaveAttributeAsync("aria-current", "page");

            Directory.Exists(output).ShouldBeTrue("the -o folder should have been created");
            Directory.GetFiles(output, "*.dll").ShouldNotBeEmpty("the obfuscated assembly should be written to the -o folder");
        }
        finally
        {
            try
            {
                Directory.Delete(work, recursive: true);
            }
            catch (IOException)
            {
                // Best effort.
            }
        }
    }
}
