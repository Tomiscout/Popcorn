using System.Collections.Concurrent;

namespace Popcorn.Server.Push;

/// <summary>
/// What each open socket watches (<c>Subscribe</c> / <c>Unsubscribe</c>): the server pushes those events when they
/// change instead of the page asking again and again. A topic is an event name, plus a key for per-item ones
/// (<c>SegmentsState</c> + item id). Subscriptions belong to the connection: the client sends them again after a
/// reconnect.
/// </summary>
public sealed class SocketSubscriptions
{
    private readonly ConcurrentDictionary<WebSocketBus, ConcurrentDictionary<string, byte>> _connections = new();

    public static string Topic(string eventType, string? key) => string.IsNullOrEmpty(key) ? eventType : $"{eventType}:{key}";

    public void Add(WebSocketBus bus, string topic)
    {
        _connections.GetOrAdd(bus, _ => new()).TryAdd(topic, 0);
    }

    public void Remove(WebSocketBus bus, string topic)
    {
        if (_connections.TryGetValue(bus, out var topics)) topics.TryRemove(topic, out _);
    }

    public void RemoveConnection(WebSocketBus bus) => _connections.TryRemove(bus, out _);

    /// <summary>Keys of the per-item topics of <paramref name="eventType"/> anyone watches.</summary>
    public List<string> Keys(string eventType)
    {
        var prefix = eventType + ":";
        return _connections.Values.SelectMany(t => t.Keys).Where(t => t.StartsWith(prefix, StringComparison.Ordinal))
            .Select(t => t[prefix.Length..]).Distinct().ToList();
    }

    public List<WebSocketBus> Subscribers(string topic) =>
        _connections.Where(c => c.Value.ContainsKey(topic)).Select(c => c.Key).ToList();
}
