using Popcorn.Plugins;

namespace Popcorn.Server.PluginHost;

/// <summary>
/// The variables plugins share (<see cref="IPluginVariables"/>): the app is the mediator, plugins never see each other.
/// Each value remembers which loaded plugin instance set it, so it disappears when that instance is unloaded.
/// </summary>
public sealed class PluginVariableStore(ILogger<PluginVariableStore> logger)
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, (string Value, PluginVariables Owner)> _values = new(StringComparer.Ordinal);

    /// <summary>The view one plugin instance gets; dispose it (<see cref="Release"/>) when the instance is unloaded.</summary>
    public PluginVariables For(string pluginId) => new(this, pluginId);

    public string? Get(string name)
    {
        lock (_gate) return _values.TryGetValue(name, out var entry) ? entry.Value : null;
    }

    public IReadOnlyDictionary<string, string> GetAll()
    {
        lock (_gate) return _values.ToDictionary(e => e.Key, e => e.Value.Value, StringComparer.Ordinal);
    }

    internal void Set(PluginVariables owner, string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Variable name must not be empty", nameof(name));
        lock (_gate)
        {
            if (owner.Released) return; // a call still running in an unloaded instance
            if (value is null)
            {
                if (_values.Remove(name, out var old))
                    logger.LogInformation("Plugin {id} removed variable {name} (set by {owner})", owner.PluginId, name, old.Owner.PluginId);
                return;
            }
            if (_values.TryGetValue(name, out var current))
            {
                if (current.Value == value && current.Owner == owner) return;
                if (current.Owner.PluginId != owner.PluginId)
                    logger.LogInformation("Plugin {id} replaced variable {name} set by {owner}", owner.PluginId, name, current.Owner.PluginId);
            }
            _values[name] = (value, owner);
            logger.LogDebug("Plugin {id} set variable {name}", owner.PluginId, name);
        }
    }

    internal void Release(PluginVariables owner)
    {
        lock (_gate)
        {
            owner.Released = true;
            foreach (var name in _values.Where(e => e.Value.Owner == owner).Select(e => e.Key).ToList())
            {
                _values.Remove(name);
                logger.LogDebug("Variable {name} removed with plugin {id}", name, owner.PluginId);
            }
        }
    }
}

/// <summary>One plugin instance's access to the <see cref="PluginVariableStore"/>.</summary>
public sealed class PluginVariables(PluginVariableStore store, string pluginId) : IPluginVariables
{
    public string PluginId { get; } = pluginId;
    internal bool Released { get; set; }

    public string? Get(string name) => store.Get(name);
    public void Set(string name, string? value) => store.Set(this, name, value);
    public IReadOnlyDictionary<string, string> GetAll() => store.GetAll();

    /// <summary>Removes the values this instance set; later <see cref="Set"/> calls are ignored.</summary>
    public void Release() => store.Release(this);
}
