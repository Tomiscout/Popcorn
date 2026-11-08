using Popcorn.Plugins;

namespace Popcorn.Server.PluginHost;

/// <summary>A status message of a plugin (<see cref="IPluginStatus"/>), as sent to the web UI.</summary>
public sealed record PluginStatusMessage(PluginStatusLevel Level, string Message);

/// <summary>
/// One loaded plugin instance's <see cref="IPluginStatus"/>. Changes call <c>onChanged</c> (the
/// <see cref="PluginManager.StatusChanged"/> event); after <see cref="Release"/> (unload) it is empty and ignores calls.
/// </summary>
public sealed class PluginStatus(string pluginId, Action onChanged, ILogger logger) : IPluginStatus
{
    private readonly Lock _gate = new();
    private readonly List<(string Key, PluginStatusMessage Message)> _messages = [];
    private bool _released;

    /// <summary>In the order the keys were first set.</summary>
    public IReadOnlyList<PluginStatusMessage> Messages
    {
        get
        {
            lock (_gate) return _messages.Select(m => m.Message).ToList();
        }
    }

    public void Set(PluginStatusLevel level, string message, string key = "")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        key ??= "";
        var entry = new PluginStatusMessage(level, message.Trim());
        lock (_gate)
        {
            if (_released) return;
            var index = _messages.FindIndex(m => m.Key == key);
            if (index >= 0)
            {
                if (_messages[index].Message == entry) return;
                _messages[index] = (key, entry);
            }
            else
            {
                _messages.Add((key, entry));
            }
        }
        logger.Log(level switch
        {
            PluginStatusLevel.Error => LogLevel.Warning,
            _ => LogLevel.Information,
        }, "Plugin {id} status {level}: {message}", pluginId, level, entry.Message);
        onChanged();
    }

    public void Clear(string key = "")
    {
        key ??= "";
        lock (_gate)
        {
            if (_released || _messages.RemoveAll(m => m.Key == key) == 0) return;
        }
        onChanged();
    }

    /// <summary>The instance is unloaded: drop the messages, ignore calls still running in it.</summary>
    internal void Release()
    {
        lock (_gate)
        {
            _released = true;
            _messages.Clear();
        }
    }
}
