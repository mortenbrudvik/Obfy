namespace Obfy.UI.Services;

/// <summary>
/// Service for displaying file and folder dialogs.
/// </summary>
public interface IFileDialogService
{
    /// <summary>
    /// Shows an open file dialog for selecting assembly files.
    /// </summary>
    /// <returns>Selected file paths, or empty array if cancelled.</returns>
    string[] ShowOpenAssemblyDialog();

    /// <summary>
    /// Shows an open file dialog for selecting source code files.
    /// </summary>
    /// <returns>Selected file paths, or empty array if cancelled.</returns>
    string[] ShowOpenSourceDialog();

    /// <summary>
    /// Shows a folder browser dialog for selecting an output directory.
    /// </summary>
    /// <returns>Selected folder path, or null if cancelled.</returns>
    string? ShowFolderBrowserDialog();

    /// <summary>
    /// Shows a save file dialog for exporting symbol maps.
    /// </summary>
    /// <returns>Selected file path, or null if cancelled.</returns>
    string? ShowSaveSymbolMapDialog();

    /// <summary>
    /// Shows an open file dialog for loading configuration files.
    /// </summary>
    /// <returns>Selected file path, or null if cancelled.</returns>
    string? ShowOpenConfigDialog();

    /// <summary>
    /// Shows a save file dialog for saving configuration files.
    /// </summary>
    /// <returns>Selected file path, or null if cancelled.</returns>
    string? ShowSaveConfigDialog();

    /// <summary>
    /// Shows a save file dialog for exporting obfuscation reports.
    /// </summary>
    /// <returns>Selected file path, or null if cancelled.</returns>
    string? ShowSaveReportDialog();
}
