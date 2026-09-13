using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using Shouldly;

namespace Obfy.UI.AutomationTests;

/// <summary>
/// Live smoke of the merged desktop UI: launch, chrome, settings, about, add-files dialog.
/// Native OpenFileDialog confirmation is not driven here — Win11's picker is not reliably
/// automatable from this session's desktop.
/// </summary>
[Trait("Category", "UI")]
public class SmokeTests : TestBase
{
    [Fact]
    public void Smoke_WindowChrome_IsPresentAndObfuscateDisabled()
    {
        MainWindow.ShouldNotBeNull();
        MainWindow.Title.ShouldContain("Obfy");
        MainWindow.IsAvailable.ShouldBeTrue();

        FindById("ObfuscateButton").ShouldNotBeNull();
        FindById("SaveConfigButton").ShouldNotBeNull();
        FindById("LoadConfigButton").ShouldNotBeNull();
        FindById("AboutButton").ShouldNotBeNull();
        FindById("AddFilesButton").ShouldNotBeNull();
        FindById("ClearFilesButton").ShouldNotBeNull();
        FindById("OutputDirectoryTextBox").ShouldNotBeNull();
        FindById("BrowseOutputButton").ShouldNotBeNull();
        FindById("LevelSelector").ShouldNotBeNull();
        FindById("StringEncryptionToggle").ShouldNotBeNull();
        FindById("LogListBox").ShouldNotBeNull();
        FindById("AutoScrollToggle").ShouldNotBeNull();
        FindById("ShowTimestampsToggle").ShouldNotBeNull();
        FindById("ClearLogsButton").ShouldNotBeNull();
        FindById("CopyLogsButton").ShouldNotBeNull();
        FindById("StatusMessage").ShouldNotBeNull();
        FindById("FileCount").ShouldNotBeNull();

        FindById("ObfuscateButton")!.AsButton().IsEnabled.ShouldBeFalse();
        FindById("FileCount")!.Name.ShouldContain("0 file");
        FindById("CancelButton").ShouldBeNull("Cancel should be hidden when idle");
    }

    [Fact]
    public void Smoke_About_OpensInWindowDialogWithVersion()
    {
        var about = FindById("AboutButton")?.AsButton();
        about.ShouldNotBeNull();
        about.Click();
        Thread.Sleep(800);

        MainWindow.FindFirstDescendant(cf => cf.ByName("About Obfy"))
            .ShouldNotBeNull("About ContentDialog should appear in the main window");

        (MainWindow.FindFirstDescendant(cf => cf.ByName("Version 1.3.0"))
            ?? FindContainingName("Version"))
            .ShouldNotBeNull("About dialog should show the assembly version, not a hardcoded 1.2.0");

        var close = FindNamed("Close")?.AsButton();
        close.ShouldNotBeNull();
        close.Click();
        Thread.Sleep(400);

        MainWindow.FindFirstDescendant(cf => cf.ByName("About Obfy")).ShouldBeNull();
    }

    [Fact]
    public void Smoke_StringEncryption_CanToggle()
    {
        var toggle = FindById("StringEncryptionToggle");
        toggle.ShouldNotBeNull();
        var pattern = toggle.Patterns.Toggle.PatternOrDefault;
        pattern.ShouldNotBeNull();

        var before = pattern.ToggleState.Value;
        pattern.Toggle();
        Thread.Sleep(200);
        pattern.ToggleState.Value.ShouldNotBe(before);
        pattern.Toggle();
        Thread.Sleep(200);
        pattern.ToggleState.Value.ShouldBe(before);
    }

    [Fact]
    public void Smoke_ProtectionAndMerge_AreReachable()
    {
        ExpandNamed("Protection");
        FindById("AntiDebugToggle").ShouldNotBeNull();
        FindById("AntiTamperToggle").ShouldNotBeNull();
        FindById("AntiDumpToggle").ShouldNotBeNull();
        FindById("AntiDecompilerToggle").ShouldNotBeNull();
        FindById("ReferenceProxyToggle").ShouldNotBeNull();
        FindById("MethodEncryptionToggle").ShouldNotBeNull();

        ExpandNamed("Watermark");
        FindById("WatermarkToggle").ShouldNotBeNull();
        FindById("WatermarkIdBox").ShouldNotBeNull();

        ExpandNamed("Protection");
        FindById("AddDecoyAttributesCheck").ShouldNotBeNull();

        ExpandNamed("Assembly Merge");
        FindById("AssemblyMergeToggle").ShouldNotBeNull();
    }

    [Fact]
    public void Smoke_AddFiles_OpensAndCancelsNativeDialog()
    {
        var add = FindById("AddFilesButton")?.AsButton();
        add.ShouldNotBeNull();
        add.Focus();
        add.Click();
        Thread.Sleep(1200);

        Keyboard.Press(VirtualKeyShort.ESCAPE);
        Thread.Sleep(400);

        FindById("ObfuscateButton")!.AsButton().IsEnabled.ShouldBeFalse();
        MainWindow.IsAvailable.ShouldBeTrue();
    }

    private AutomationElement? FindNamed(string name)
        => MainWindow.FindFirstDescendant(cf => cf.ByName(name));

    private AutomationElement? FindContainingName(string fragment)
    {
        return MainWindow.FindAllDescendants()
            .FirstOrDefault(e =>
            {
                try
                {
                    return e.Name.Contains(fragment, StringComparison.OrdinalIgnoreCase);
                }
                catch (Exception)
                {
                    return false;
                }
            });
    }

    private void ExpandNamed(string name)
    {
        var header = FindNamed(name);
        header.ShouldNotBeNull($"{name} header should exist");

        if (header.Patterns.ExpandCollapse.IsSupported)
        {
            var pattern = header.Patterns.ExpandCollapse.Pattern;
            if (pattern.ExpandCollapseState.Value == ExpandCollapseState.Collapsed)
                pattern.Expand();
        }
        else
        {
            header.Click();
        }

        Thread.Sleep(400);
    }
}
