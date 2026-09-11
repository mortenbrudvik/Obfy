namespace Obfy.UI.Services;

/// <summary>
/// Abstraction over the system clipboard so ViewModels stay testable.
/// </summary>
public interface IClipboardService
{
    void SetText(string text);
}
