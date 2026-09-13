using FlaUI.Core.AutomationElements;
using Shouldly;

namespace Obfy.UI.AutomationTests;

/// <summary>
/// Tests for application launch and basic control presence.
/// </summary>
[Trait("Category", "UI")]
public class ApplicationLaunchTests : TestBase
{
    [Fact]
    public void Application_Launches_Successfully()
    {
        MainWindow.ShouldNotBeNull();
        MainWindow.Title.ShouldContain("Obfy");
    }

    [Fact]
    public void MainWindow_Has_Toolbar_Controls()
    {
        FindById("ObfuscateButton").ShouldNotBeNull("ObfuscateButton should exist");
        FindById("SaveConfigButton").ShouldNotBeNull("SaveConfigButton should exist");
        FindById("LoadConfigButton").ShouldNotBeNull("LoadConfigButton should exist");
        FindById("AboutButton").ShouldNotBeNull("AboutButton should exist");
        FindById("HelpButton").ShouldNotBeNull("HelpButton should exist");
    }

    [Fact]
    public void MainWindow_Has_Files_Panel_Controls()
    {
        FindById("AddFilesButton").ShouldNotBeNull("AddFilesButton should exist");
        FindById("ClearFilesButton").ShouldNotBeNull("ClearFilesButton should exist");
        // FilesList may be virtualized/collapsed when empty
        FindById("OutputDirectoryTextBox").ShouldNotBeNull("OutputDirectoryTextBox should exist");
        FindById("BrowseOutputButton").ShouldNotBeNull("BrowseOutputButton should exist");
    }

    [Fact]
    public void MainWindow_Has_Settings_Panel_Controls()
    {
        FindById("LevelSelector").ShouldNotBeNull("LevelSelector should exist");
        FindById("StringEncryptionToggle").ShouldNotBeNull("StringEncryptionToggle should exist");
    }

    [Fact]
    public void MainWindow_Has_Output_Panel_Controls()
    {
        FindById("LogListBox").ShouldNotBeNull("LogListBox should exist");
        FindById("AutoScrollToggle").ShouldNotBeNull("AutoScrollToggle should exist");
        FindById("ClearLogsButton").ShouldNotBeNull("ClearLogsButton should exist");
        FindById("CopyLogsButton").ShouldNotBeNull("CopyLogsButton should exist");
    }

    [Fact]
    public void MainWindow_Has_Status_Bar_Controls()
    {
        FindById("StatusMessage").ShouldNotBeNull("StatusMessage should exist");
        FindById("FileCount").ShouldNotBeNull("FileCount should exist");
    }

    [Fact]
    public void ObfuscateButton_Disabled_When_NoFiles()
    {
        var button = FindById("ObfuscateButton")?.AsButton();
        button.ShouldNotBeNull();
        button.IsEnabled.ShouldBeFalse("ObfuscateButton should be disabled when no files are loaded");
    }
}
