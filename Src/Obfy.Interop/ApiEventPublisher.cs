using Obfy.App.Services;

namespace Obfy.Interop;

/// <summary>
/// <see cref="IEventPublisher"/> over <see cref="ApiEventAggregator"/>. The aggregator needs the web view, which is a WPF
/// control built on the UI thread, so it is resolved on the first publish and not when a service that publishes is
/// created: a session resolved on a pool thread must not be what builds the control.
/// </summary>
public sealed class ApiEventPublisher(Lazy<ApiEventAggregator> events) : IEventPublisher
{
    public void Publish(string eventName, object? data) => events.Value.Publish(eventName, data);
}
