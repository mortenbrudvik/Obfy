using FlaUI.Core.AutomationElements;

namespace Obfy.UI.AutomationTests.Elements;

/// <summary>
/// Page object model for the main window elements.
/// </summary>
public class MainWindowElements
{
    private readonly Window _window;

    public MainWindowElements(Window window) => _window = window;

    // Toolbar buttons
    public Button? ObfuscateButton => FindButton("ObfuscateButton");
    public Button? CancelButton => FindButton("CancelButton");
    public Button? SaveConfigButton => FindButton("SaveConfigButton");
    public Button? LoadConfigButton => FindButton("LoadConfigButton");
    public Button? AboutButton => FindButton("AboutButton");

    // Progress
    public AutomationElement? OverallProgressBar => Find("OverallProgressBar");
    public AutomationElement? ProgressText => Find("ProgressText");

    // Status
    public AutomationElement? StatusMessage => Find("StatusMessage");
    public AutomationElement? FileCount => Find("FileCount");

    // Files Panel
    public Button? AddFilesButton => FindButton("AddFilesButton");
    public Button? ClearFilesButton => FindButton("ClearFilesButton");
    public AutomationElement? FilesList => Find("FilesList");
    public AutomationElement? OutputDirectoryTextBox => Find("OutputDirectoryTextBox");
    public Button? BrowseOutputButton => FindButton("BrowseOutputButton");

    // Settings Panel
    public ComboBox? LevelSelector => Find("LevelSelector")?.AsComboBox();
    public AutomationElement? StringEncryptionToggle => Find("StringEncryptionToggle");
    public AutomationElement? ConstantEncryptionToggle => Find("ConstantEncryptionToggle");
    public AutomationElement? ControlFlowToggle => Find("ControlFlowToggle");
    public AutomationElement? SymbolRenamingToggle => Find("SymbolRenamingToggle");
    public AutomationElement? AntiDebugToggle => Find("AntiDebugToggle");
    public AutomationElement? AntiTamperToggle => Find("AntiTamperToggle");
    public AutomationElement? AntiDumpToggle => Find("AntiDumpToggle");
    public AutomationElement? PackingToggle => Find("PackingToggle");
    public AutomationElement? ResourceEncryptionToggle => Find("ResourceEncryptionToggle");

    // Output Panel
    public AutomationElement? LogListBox => Find("LogListBox");
    public AutomationElement? AutoScrollToggle => Find("AutoScrollToggle");
    public Button? ClearLogsButton => FindButton("ClearLogsButton");
    public Button? CopyLogsButton => FindButton("CopyLogsButton");

    // Results Panel
    public AutomationElement? ElapsedTimeText => Find("ElapsedTimeText");
    public Button? ExportReportButton => FindButton("ExportReportButton");
    public Button? ExportMapButton => FindButton("ExportMapButton");
    public AutomationElement? SymbolSearchBox => Find("SymbolSearchBox");
    public AutomationElement? SymbolTree => Find("SymbolTree");

    private Button? FindButton(string id) => Find(id)?.AsButton();

    private AutomationElement? Find(string id) =>
        _window.FindFirstDescendant(cf => cf.ByAutomationId(id));
}
