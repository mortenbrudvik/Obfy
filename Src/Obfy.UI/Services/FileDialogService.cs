using Microsoft.Win32;

namespace Obfy.UI.Services;

/// <summary>
/// Implementation of file dialog service using Win32 dialogs.
/// </summary>
public class FileDialogService : IFileDialogService
{
    public string[] ShowOpenAssemblyDialog()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select Assembly Files",
            Filter = "Assemblies, source, and solutions (*.dll;*.exe;*.cs;*.sln;*.slnx;*.csproj;*.vbproj;*.fsproj)|*.dll;*.exe;*.cs;*.sln;*.slnx;*.csproj;*.vbproj;*.fsproj|Assembly Files (*.dll;*.exe)|*.dll;*.exe|C# Files (*.cs)|*.cs|All Files (*.*)|*.*",
            Multiselect = true
        };

        return dialog.ShowDialog() == true ? dialog.FileNames : Array.Empty<string>();
    }

    public string[] ShowOpenSourceDialog()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select Source Files",
            Filter = "C# Files (*.cs)|*.cs|All Files (*.*)|*.*",
            Multiselect = true
        };

        return dialog.ShowDialog() == true ? dialog.FileNames : Array.Empty<string>();
    }

    public string? ShowFolderBrowserDialog()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select Output Directory"
        };

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    public string? ShowSaveSymbolMapDialog()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export Symbol Map",
            Filter = "JSON Files (*.json)|*.json|Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
            DefaultExt = ".json",
            FileName = "symbolmap.json"
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? ShowOpenConfigDialog()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Load Configuration",
            Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*"
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? ShowSaveConfigDialog()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Save Configuration",
            Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
            DefaultExt = ".json",
            FileName = "obfy-config.json"
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? ShowSaveReportDialog()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export Obfuscation Report",
            Filter = "HTML Report (*.html)|*.html|JSON Report (*.json)|*.json",
            DefaultExt = ".html",
            FileName = "obfuscation-report.html"
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
