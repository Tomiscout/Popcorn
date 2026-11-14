using Popcorn.Plugins;

namespace Popcorn.Server.PluginHost;

/// <summary>
/// The events plugins send each other (<see cref="IPluginEvents"/>): the app is the mediator, plugins never see each
/// other. Each subscription remembers the loaded plugin instance that made it and ends when that instance is unloaded.
/// </summary>
public sealed class PluginEventHub(ILogger<PluginEventHub> logger)
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, List<Subscription>> _subscriptions = new(StringComparer.Ordinal);

    /// <summary>The view one plugin instance gets; <see cref="PluginEvents.Release"/> it when the instance is unloaded.</summary>
    public PluginEvents For(string pluginId) => new(this, pluginId);

    internal int Publish(PluginEvents sender, string name, string? payload)
    {
        CheckName(name);
        Subscription[] targets;
        lock (_gate)
        {
            if (sender.Released) return 0; // a call still running in an unloaded instance
            targets = _subscriptions.TryGetValue(name, out var list) ? [.. list] : [];
        }
        logger.LogDebug("Plugin {id} sent {name} to {count} subscriber(s)", sender.PluginId, name, targets.Length);
        foreach (var target in targets)
        {
            _ = Task.Run(() =>
            {
                if (target.Disposed) return;
                try
                {
                    target.Handler(payload);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Plugin {id}: handler of {name} (sent by {sender}) failed", target.Owner.PluginId, name, sender.PluginId);
                }
            });
        }
        return targets.Length;
    }

    internal IDisposable Subscribe(PluginEvents owner, string name, Action<string?> handler)
    {
        CheckName(name);
        ArgumentNullException.ThrowIfNull(handler);
        var subscription = new Subscription(this, owner, name, handler);
        lock (_gate)
        {
            if (owner.Released)
            {
                subscription.Disposed = true;
                return subscription;
            }
            if (!_subscriptions.TryGetValue(name, out var list)) _subscriptions[name] = list = [];
            list.Add(subscription);
        }
        logger.LogDebug("Plugin {id} subscribed to {name}", owner.PluginId, name);
        return subscription;
    }

    private void Remove(Subscription subscription)
    {
        lock (_gate)
        {
            subscription.Disposed = true;
            if (!_subscriptions.TryGetValue(subscription.Name, out var list)) return;
            list.Remove(subscription);
            if (list.Count == 0) _subscriptions.Remove(subscription.Name);
        }
    }

    internal void Release(PluginEvents owner)
    {
        lock (_gate)
        {
            owner.Released = true;
            foreach (var (name, list) in _subscriptions.ToList())
            {
                foreach (var subscription in list.Where(s => s.Owner == owner)) subscription.Disposed = true;
                list.RemoveAll(s => s.Owner == owner);
                if (list.Count == 0) _subscriptions.Remove(name);
            }
        }
    }

    private static void CheckName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Event name must not be empty", nameof(name));
    }

    private sealed class Subscription(PluginEventHub hub, PluginEvents owner, string name, Action<string?> handler) : IDisposable
    {
        public PluginEvents Owner { get; } = owner;
        public string Name { get; } = name;
        public Action<string?> Handler { get; } = handler;
        public volatile bool Disposed;

        public void Dispose() => hub.Remove(this);
    }
}

/// <summary>One plugin instance's access to the <see cref="PluginEventHub"/>.</summary>
public sealed class PluginEvents(PluginEventHub hub, string pluginId) : IPluginEvents
{
    public string PluginId { get; } = pluginId;
    internal bool Released { get; set; }

    public int Publish(string name, string? payload = null) => hub.Publish(this, name, payload);
    public IDisposable Subscribe(string name, Action<string?> handler) => hub.Subscribe(this, name, handler);

    /// <summary>Ends this instance's subscriptions; later calls do nothing.</summary>
    public void Release() => hub.Release(this);
}
