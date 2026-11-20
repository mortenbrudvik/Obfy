using System.ComponentModel;
using System.Diagnostics;

namespace Obfy.App.Services;

/// <summary>Opens a folder, or a folder with a file selected, in the system file browser.</summary>
public interface IShellLauncher
{
    /// <exception cref="IOException">The folder could not be opened.</exception>
    void OpenFolder(string path);

    /// <exception cref="IOException">The file browser could not be started.</exception>
    void RevealFile(string path);
}

/// <summary>Windows Explorer behind <see cref="IShellLauncher"/>.</summary>
public sealed class ShellLauncher : IShellLauncher
{
    public void OpenFolder(string path) => Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });

    public void RevealFile(string path) =>
        Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = $"/select,\"{path}\"", UseShellExecute = true });

    private static void Start(ProcessStartInfo info)
    {
        try
        {
            Process.Start(info);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            throw new IOException(ex.Message, ex);
        }
    }
}
