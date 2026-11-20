using Obfy.App.Sessions;

namespace Obfy.Interop;

/// <summary>
/// Pushes <c>settings.changed</c> with the new snapshot whenever <see cref="ProtectionSession"/> changes, whoever changed
/// it: the page, a loaded configuration or the host. It lives here, not in the handlers, because Obfy.App has no
/// knowledge of the web view. A singleton that must be created once at startup so it subscribes.
/// </summary>
public sealed class SettingsEventForwarder
{
    public SettingsEventForwarder(ProtectionSession session, ApiEventAggregator events)
    {
        session.Changed += () => events.Publish(IpcNames.Events.SettingsChanged, session.GetSnapshot());
    }
}
