namespace Obfy.App.Features.App;

/// <summary>
/// Carries "the page is ready" from the <c>app.ready</c> handler to whoever shows the window. A singleton. <see cref="Ready"/>
/// is raised once, on the thread that handled the call (not the UI thread); a subscriber that comes late is not missed
/// because it can read <see cref="IsReady"/> after subscribing.
/// </summary>
public class AppReadyCoordinator
{
    private int _signalled;

    public bool IsReady => Volatile.Read(ref _signalled) == 1;

    public event Action? Ready;

    /// <summary>Raises <see cref="Ready"/> the first time it is called; later calls (a page reload) do nothing.</summary>
    public void SignalReady()
    {
        if (Interlocked.Exchange(ref _signalled, 1) == 0)
            Ready?.Invoke();
    }
}
