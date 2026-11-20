using System.IO;
using System.Windows;
using Microsoft.Win32;
using Obfy.App.Services;

namespace Obfy.UI.Services;

/// <summary>
/// The Win32 dialogs behind <see cref="IFileDialogs"/>. Handlers call in from pool threads, so every dialog is shown on the
/// UI thread, owned by the main window when there is one.
/// </summary>
public class FileDialogs : IFileDialogs
{
    private const string KeyFilter = "Strong-name keys (*.snk;*.pfx;*.p12)|*.snk;*.pfx;*.p12|All Files (*.*)|*.*";

    public Task<string[]> PickFilesAsync(string title, string filter, bool multi) =>
        OnUiThread(() =>
        {
            var dialog = new OpenFileDialog { Title = title, Filter = filter, Multiselect = multi };
            return Show(dialog) ? dialog.FileNames : [];
        });

    public Task<string?> PickFolderAsync(string title) =>
        OnUiThread(() =>
        {
            var dialog = new OpenFolderDialog { Title = title };
            return Show(dialog) ? dialog.FolderName : null;
        });

    public Task<string?> PickSaveAsync(string title, string filter, string? defaultName) =>
        OnUiThread(() =>
        {
            var dialog = new SaveFileDialog { Title = title, Filter = filter };
            if (!string.IsNullOrEmpty(defaultName))
            {
                dialog.FileName = defaultName;
                dialog.DefaultExt = Path.GetExtension(defaultName);
            }

            return Show(dialog) ? dialog.FileName : null;
        });

    public Task<string?> PickKeyAsync() =>
        OnUiThread(() =>
        {
            var dialog = new OpenFileDialog { Title = "Select Strong-Name Key", Filter = KeyFilter, Multiselect = false };
            return Show(dialog) ? dialog.FileName : null;
        });

    private static bool Show(CommonDialog dialog)
    {
        var owner = Application.Current?.MainWindow;
        return (owner is { IsLoaded: true } ? dialog.ShowDialog(owner) : dialog.ShowDialog()) == true;
    }

    private static Task<T> OnUiThread<T>(Func<T> pick)
    {
        var dispatcher = Application.Current?.Dispatcher
            ?? throw new InvalidOperationException("No WPF application is running to host the dialog.");
        return dispatcher.InvokeAsync(pick).Task;
    }
}
