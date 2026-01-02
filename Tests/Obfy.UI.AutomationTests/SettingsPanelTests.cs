using FlaUI.Core.AutomationElements;
using Shouldly;

namespace Obfy.UI.AutomationTests;

/// <summary>
/// Tests for the Settings panel functionality.
/// </summary>
public class SettingsPanelTests : TestBase
{
    [Fact]
    public void LevelSelector_Is_Present()
    {
        var combo = FindById("LevelSelector")?.AsComboBox();
        combo.ShouldNotBeNull("LevelSelector should exist");
    }

    [Fact]
    public void LevelSelector_Has_Items()
    {
        var combo = FindById("LevelSelector")?.AsComboBox();
        combo.ShouldNotBeNull();
        combo.Items.Length.ShouldBeGreaterThan(0, "LevelSelector should have items");
    }

    [Fact]
    public void StringEncryptionToggle_Exists()
    {
        // StringEncryption expander is expanded by default
        var toggle = FindById("StringEncryptionToggle");
        toggle.ShouldNotBeNull("StringEncryptionToggle should exist");
    }

    // Note: Other toggle switches are inside collapsed Expanders
    // and won't be in the visual tree until expanded.
    // These would require expanding the parent Expander first.

    [Fact]
    public void StringEncryptionToggle_CanBeToggled()
    {
        var toggle = FindById("StringEncryptionToggle");
        toggle.ShouldNotBeNull();

        // Get initial state
        var initialPattern = toggle.Patterns.Toggle.PatternOrDefault;
        if (initialPattern == null)
        {
            // Skip if toggle pattern not supported
            return;
        }

        var initialState = initialPattern.ToggleState.Value;

        // Toggle
        initialPattern.Toggle();
        Thread.Sleep(200);

        // Verify state changed
        var newState = initialPattern.ToggleState.Value;
        newState.ShouldNotBe(initialState);
    }
}
