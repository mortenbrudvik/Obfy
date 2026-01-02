using System.Threading.Tasks;

namespace Obfy.VisualStudio.Services;

/// <summary>
/// Service for logging to the Visual Studio Output window
/// </summary>
public interface IOutputService
{
    /// <summary>
    /// Log an informational message
    /// </summary>
    void Info(string message);

    /// <summary>
    /// Log a warning message
    /// </summary>
    void Warning(string message);

    /// <summary>
    /// Log an error message
    /// </summary>
    void Error(string message);

    /// <summary>
    /// Log a success message
    /// </summary>
    void Success(string message);

    /// <summary>
    /// Log a message asynchronously
    /// </summary>
    Task WriteLineAsync(string message);

    /// <summary>
    /// Clear the output pane
    /// </summary>
    Task ClearAsync();

    /// <summary>
    /// Activate (show) the output pane
    /// </summary>
    Task ActivateAsync();
}
