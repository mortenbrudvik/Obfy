namespace Obfy.App.Services;

/// <summary>The native file and folder pickers, for handlers that run off the UI thread. Each answers null / empty when cancelled.</summary>
public interface IFileDialogs
{
    /// <param name="filter">A Win32 filter string, for example <c>"Assemblies (*.dll;*.exe)|*.dll;*.exe|All files (*.*)|*.*"</c>.</param>
    Task<string[]> PickFilesAsync(string title, string filter, bool multi);

    Task<string?> PickFolderAsync(string title);

    Task<string?> PickSaveAsync(string title, string filter, string? defaultName);

    /// <summary>Picks a strong-name key file (.snk, .pfx, .p12).</summary>
    Task<string?> PickKeyAsync();
}
