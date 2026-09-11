namespace Obfy.UI.Services;

/// <summary>
/// Marshals work onto the WPF UI thread. Runs inline when no dispatcher is available (unit tests).
/// </summary>
public interface IUiDispatcher
{
    void Invoke(Action action);

    Task InvokeAsync(Action action);
}
