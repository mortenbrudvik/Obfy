using System.ComponentModel;
using System.Diagnostics;

namespace Obfy.App.Services;

/// <summary>Opens a folder, or a folder with a file selected, in the system file browser, and a web address in the default browser.</summary>
public interface IShellLauncher
{
    /// <exception cref="IOException">The folder could not be opened.</exception>
    void OpenFolder(string path);

    /// <exception cref="IOException">The file browser could not be started.</exception>
    void RevealFile(string path);

    /// <summary>Opens <paramref name="url"/> in the default browser. Only an absolute <c>https</c> address is accepted.</summary>
    /// <exception cref="ArgumentException">The address is not an absolute <c>https</c> address.</exception>
    /// <exception cref="IOException">The browser could not be started.</exception>
    void OpenUrl(Uri url);
}

/// <summary>Windows Explorer and the default browser behind <see cref="IShellLauncher"/>.</summary>
public sealed class ShellLauncher : IShellLauncher
{
    public void OpenFolder(string path) => Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });

    public void RevealFile(string path) =>
        Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = $"/select,\"{path}\"", UseShellExecute = true });

    public void OpenUrl(Uri url)
    {
        // Shell execution runs whatever the scheme names (file:, ms-settings:, a custom protocol), so anything but https is refused.
        if (url is null || !url.IsAbsoluteUri || url.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("Only an absolute https address can be opened.", nameof(url));

        Start(new ProcessStartInfo { FileName = url.AbsoluteUri, UseShellExecute = true });
    }

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
