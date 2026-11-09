using System.Reflection;
using Microsoft.Extensions.Logging;
using Olbrasoft.SystemTray.Linux;
using Tmds.DBus.Protocol;
using Tmds.DBus.SourceGenerator;

namespace Popcorn.Platform.Linux;

internal sealed class PopcornTrayMenuHandler : ComCanonicalDbusmenuHandler, ITrayMenuHandler
{
    private const string MenuPath = "/MenuBar";
    private const uint MenuRevision = 1;

    private static readonly ConstructorInfo DictionaryVariantConstructor = typeof(VariantValue)
        .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
        .Single(static ctor =>
        {
            var parameters = ctor.GetParameters();
            return parameters.Length == 5
                && parameters[0].ParameterType == typeof(VariantValueType)
                && parameters[1].ParameterType == typeof(VariantValueType)
                && parameters[3].ParameterType == typeof(KeyValuePair<VariantValue, VariantValue>[]);
        });

    private readonly ILogger<PopcornTrayMenuHandler> _logger;
    private readonly DbusMenuNode _root;
    private readonly Dictionary<int, DbusMenuNode> _nodes;

    private Connection? _connection;

    public PopcornTrayMenuHandler(
        ILogger<PopcornTrayMenuHandler> logger,
        IEnumerable<DbusMenuNode> items)
        : base(emitOnCapturedContext: false)
    {
        _logger = logger;
        _root = new DbusMenuItem(0, string.Empty, children: items);
        _nodes = BuildIndex(_root);

        Version = 4;
        TextDirection = "ltr";
        Status = "normal";
        IconThemePath = [];
    }

    public override Connection Connection =>
        _connection ?? throw new InvalidOperationException("Menu handler not registered.");

    public void RegisterWithDbus(Connection connection)
    {
        if (PathHandler is not null)
        {
            return;
        }

        _connection = connection;

        var pathHandler = new PathHandler(MenuPath);
        pathHandler.Add(this);
        PathHandler = pathHandler;
        connection.AddMethodHandler(pathHandler);

        _logger.LogDebug("Registered DBus menu handler at {MenuPath}", MenuPath);
    }

    public void UnregisterFromDbus(Connection connection)
    {
        var pathHandler = PathHandler;
        if (pathHandler is null)
        {
            return;
        }

        pathHandler.Remove(this);
        connection.RemoveMethodHandler(pathHandler.Path);
        PathHandler = null;
        _connection = null;

        _logger.LogDebug("Unregistered DBus menu handler");
    }

    protected override ValueTask<(uint Revision, (int, Dictionary<string, VariantValue>, VariantValue[]) Layout)>
        OnGetLayoutAsync(Message request, int parentId, int recursionDepth, string[] propertyNames)
    {
        _ = request;

        if (!_nodes.TryGetValue(parentId, out var node))
        {
            node = _root;
        }

        return ValueTask.FromResult(BuildLayout(node, recursionDepth, propertyNames));
    }

    protected override ValueTask<(int, Dictionary<string, VariantValue>)[]> OnGetGroupPropertiesAsync(
        Message request,
        int[] ids,
        string[] propertyNames)
    {
        _ = request;

        var items = ids
            .Where(id => _nodes.ContainsKey(id))
            .Select(id => (Id: id, Properties: FilterProperties(GetProperties(_nodes[id]), propertyNames)))
            .ToArray();

        return ValueTask.FromResult(items);
    }

    protected override ValueTask<VariantValue> OnGetPropertyAsync(Message request, int id, string name)
    {
        _ = request;

        if (!_nodes.TryGetValue(id, out var node))
        {
            throw new DBusException("org.freedesktop.DBus.Error.InvalidArgs", $"Item '{id}' not found.");
        }

        var properties = GetProperties(node);
        if (!properties.TryGetValue(name, out var value))
        {
            throw new DBusException(
                "org.freedesktop.DBus.Error.InvalidArgs",
                $"Property '{name}' not found for item '{id}'.");
        }

        return ValueTask.FromResult(value);
    }

    protected override ValueTask OnEventAsync(Message request, int id, string eventId, VariantValue data, uint timestamp)
    {
        if (eventId != "clicked" || !_nodes.TryGetValue(id, out var node))
        {
            return ValueTask.CompletedTask;
        }

        if (node is DbusMenuItem item && item.OnClick is not null)
        {
            _logger.LogDebug("DBusMenu clicked item {Id} ({Label})", id, item.Label);
            EmitItemActivationRequested(id, timestamp);
            _ = Task.Run(item.OnClick);
        }

        return ValueTask.CompletedTask;
    }

    protected override ValueTask<int[]> OnEventGroupAsync(Message request, (int, string, VariantValue, uint)[] events)
    {
        var idErrors = new List<int>();

        foreach (var (id, eventId, data, timestamp) in events)
        {
            if (!_nodes.ContainsKey(id))
            {
                idErrors.Add(id);
                continue;
            }

            _ = OnEventAsync(request, id, eventId, data, timestamp);
        }

        return ValueTask.FromResult(idErrors.ToArray());
    }

    protected override ValueTask<bool> OnAboutToShowAsync(Message request, int id)
    {
        _ = request;
        _ = id;
        return ValueTask.FromResult(false);
    }

    protected override ValueTask<(int[] UpdatesNeeded, int[] IdErrors)> OnAboutToShowGroupAsync(
        Message request,
        int[] ids)
    {
        _ = request;
        var idErrors = ids.Where(id => !_nodes.ContainsKey(id)).ToArray();

        return ValueTask.FromResult<(int[] UpdatesNeeded, int[] IdErrors)>(([], idErrors));
    }

    private (uint Revision, (int, Dictionary<string, VariantValue>, VariantValue[]) Layout) BuildLayout(
        DbusMenuNode node,
        int recursionDepth,
        string[] propertyNames)
    {
        var properties = FilterProperties(GetProperties(node), propertyNames);
        var children = recursionDepth == 0
            ? []
            : node.Children
                .Where(static child => child.Visible)
                .Select(child => BuildLayoutVariant(child, NextDepth(recursionDepth), propertyNames))
                .ToArray();

        return (MenuRevision, (node.Id, properties, children));
    }

    private VariantValue BuildLayoutVariant(DbusMenuNode node, int recursionDepth, string[] propertyNames)
    {
        var properties = FilterProperties(GetProperties(node), propertyNames);
        var children = recursionDepth == 0
            ? []
            : node.Children
                .Where(static child => child.Visible)
                .Select(child => BuildLayoutVariant(child, NextDepth(recursionDepth), propertyNames))
                .ToArray();

        return VariantValue.Struct(
            VariantValue.Int32(node.Id),
            CreatePropertyDictionaryVariant(properties),
            VariantValue.ArrayOfVariant(children));
    }

    // Tmds.DBus.Protocol has no public factory for a{sv}, so build same shape generator expects.
    private static VariantValue CreatePropertyDictionaryVariant(Dictionary<string, VariantValue> properties)
    {
        var items = properties
            .Select(static pair => new KeyValuePair<VariantValue, VariantValue>(
                VariantValue.String(pair.Key),
                pair.Value))
            .ToArray();

        return (VariantValue)DictionaryVariantConstructor.Invoke(
            [
                VariantValueType.String,
                VariantValueType.Variant,
                null!,
                items,
                (byte)0
            ]);
    }

    private static int NextDepth(int recursionDepth) => recursionDepth < 0 ? -1 : recursionDepth - 1;

    private static Dictionary<string, VariantValue> GetProperties(DbusMenuNode node)
    {
        if (node.Id == 0)
        {
            return new Dictionary<string, VariantValue>
            {
                ["children-display"] = VariantValue.String("submenu"),
            };
        }

        if (node is DbusSeparator)
        {
            return new Dictionary<string, VariantValue>
            {
                ["type"] = VariantValue.String("separator"),
                ["visible"] = VariantValue.Bool(node.Visible),
            };
        }

        var item = (DbusMenuItem)node;
        var props = new Dictionary<string, VariantValue>
        {
            ["type"] = VariantValue.String("standard"),
            ["label"] = VariantValue.String(item.Label),
            ["enabled"] = VariantValue.Bool(item.Enabled),
            ["visible"] = VariantValue.Bool(item.Visible),
        };

        if (item.Children.Count > 0)
        {
            props["children-display"] = VariantValue.String("submenu");
        }

        return props;
    }

    private static Dictionary<string, VariantValue> FilterProperties(
        Dictionary<string, VariantValue> properties,
        string[] propertyNames)
    {
        if (propertyNames.Length == 0)
        {
            return properties;
        }

        var names = new HashSet<string>(propertyNames);
        return properties
            .Where(pair => names.Contains(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    private static Dictionary<int, DbusMenuNode> BuildIndex(DbusMenuNode root)
    {
        var dict = new Dictionary<int, DbusMenuNode>();
        var stack = new Stack<DbusMenuNode>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var node = stack.Pop();
            dict[node.Id] = node;

            foreach (var child in node.Children)
            {
                stack.Push(child);
            }
        }

        return dict;
    }
}
