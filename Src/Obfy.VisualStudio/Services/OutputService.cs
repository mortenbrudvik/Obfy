using System;
using System.Threading.Tasks;
using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Shell;

namespace Obfy.VisualStudio.Services;

/// <summary>
/// Implementation of output service using VS Output window
/// </summary>
public class OutputService : IOutputService
{
    private const string PaneName = "Obfy";
    private OutputWindowPane? _pane;

    /// <summary>
    /// Initialize the output pane
    /// </summary>
    public async Task InitializeAsync()
    {
        _pane = await VS.Windows.CreateOutputWindowPaneAsync(PaneName, lazyCreate: false);
    }

    public void Info(string message)
    {
        _ = WriteLineInternalAsync(message, "INFO");
    }

    public void Warning(string message)
    {
        _ = WriteLineInternalAsync(message, "WARN");
    }

    public void Error(string message)
    {
        _ = WriteLineInternalAsync(message, "ERROR");
    }

    public void Success(string message)
    {
        _ = WriteLineInternalAsync(message, "OK");
    }

    public async Task WriteLineAsync(string message)
    {
        await WriteLineInternalAsync(message, null);
    }

    public async Task ClearAsync()
    {
        if (_pane != null)
        {
            await _pane.ClearAsync();
        }
    }

    public async Task ActivateAsync()
    {
        if (_pane != null)
        {
            await _pane.ActivateAsync();
        }
    }

    private async Task WriteLineInternalAsync(string message, string? level)
    {
        if (_pane == null)
        {
            return;
        }

        try
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            var formattedMessage = level != null
                ? $"[{timestamp}] [{level}] {message}"
                : $"[{timestamp}] {message}";

            await _pane.WriteLineAsync(formattedMessage);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to write to Obfy output pane: {ex.Message}");
        }
    }
}
