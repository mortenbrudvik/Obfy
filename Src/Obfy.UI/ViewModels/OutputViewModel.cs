using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Obfy.UI.Models;

namespace Obfy.UI.ViewModels;

/// <summary>
/// ViewModel for the output/log panel.
/// </summary>
public partial class OutputViewModel : ObservableObject
{
    /// <summary>
    /// Gets the collection of log entries.
    /// </summary>
    public ObservableCollection<LogEntry> Logs { get; } = new();

    [ObservableProperty]
    private bool _autoScroll = true;

    [ObservableProperty]
    private bool _showTimestamps = true;

    /// <summary>
    /// Adds a log entry with the specified message and level.
    /// </summary>
    public void AddLog(string message, LogLevel level = LogLevel.Info)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            Logs.Add(LogEntry.Create(message, level));
        });
    }

    /// <summary>
    /// Adds an info log entry.
    /// </summary>
    public void Info(string message) => AddLog(message, LogLevel.Info);

    /// <summary>
    /// Adds a warning log entry.
    /// </summary>
    public void Warning(string message) => AddLog(message, LogLevel.Warning);

    /// <summary>
    /// Adds an error log entry.
    /// </summary>
    public void Error(string message) => AddLog(message, LogLevel.Error);

    /// <summary>
    /// Adds a success log entry.
    /// </summary>
    public void Success(string message) => AddLog(message, LogLevel.Success);

    /// <summary>
    /// Adds a debug log entry.
    /// </summary>
    public void Debug(string message) => AddLog(message, LogLevel.Debug);

    /// <summary>
    /// Clears all log entries.
    /// </summary>
    public void Clear()
    {
        Logs.Clear();
    }

    [RelayCommand]
    private void ClearLogs() => Clear();

    /// <summary>
    /// Copies all log entries to the clipboard.
    /// </summary>
    [RelayCommand]
    private void CopyLogs()
    {
        var sb = new StringBuilder();
        foreach (var log in Logs)
        {
            if (ShowTimestamps)
            {
                sb.AppendLine($"[{log.Timestamp:HH:mm:ss.fff}] [{log.Level}] {log.Message}");
            }
            else
            {
                sb.AppendLine($"[{log.Level}] {log.Message}");
            }
        }
        Clipboard.SetText(sb.ToString());
    }
}
