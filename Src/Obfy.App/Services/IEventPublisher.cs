namespace Obfy.App.Services;

/// <summary>
/// How the application layer pushes an event to the page. The host implements it over the web view bridge; services take
/// it by constructor and publish with a name from <see cref="AppEvents"/>. <paramref name="data"/> is serialized by
/// <c>JsonUtils</c>. Safe to call from any thread, and never while holding a lock the page could be waiting on.
/// </summary>
public interface IEventPublisher
{
    void Publish(string eventName, object? data);
}
