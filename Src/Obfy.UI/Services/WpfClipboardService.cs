using System.Windows;

namespace Obfy.UI.Services;

/// <summary>
/// Clipboard implementation that forwards to <see cref="Clipboard"/>.
/// </summary>
public sealed class WpfClipboardService : IClipboardService
{
    public void SetText(string text)
    {
        try
        {
            Clipboard.SetText(text);
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
        }
    }
}
