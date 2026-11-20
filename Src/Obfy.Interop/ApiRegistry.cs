using System.Collections.Concurrent;
using Mediator;

namespace Obfy.Interop;

/// <summary>Maps a page's <c>service.method</c> call to the Mediator request type that carries it.</summary>
public class ApiRegistry
{
    private readonly ConcurrentDictionary<string, Type> _requests = new();

    /// <exception cref="InvalidOperationException">The name is already registered.</exception>
    public void Register<TRequest>(string service, string method) where TRequest : IBaseRequest
    {
        var name = $"{service}.{method}";
        if (!_requests.TryAdd(name, typeof(TRequest)))
            throw new InvalidOperationException($"{name} is already registered, to {_requests[name].Name}.");
    }

    public Type? GetRequestType(string service, string method) =>
        _requests.GetValueOrDefault($"{service}.{method}");

    /// <summary>Every registration, keyed by <c>service.method</c>. For the contract tests that pin the whole surface.</summary>
    public IReadOnlyDictionary<string, Type> GetAll() => new Dictionary<string, Type>(_requests);
}
