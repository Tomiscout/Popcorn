using Olbrasoft.SystemTray.Linux;

namespace Popcorn.Platform.Linux;

internal abstract class DbusMenuNode
{
    protected DbusMenuNode(int id)
    {
        Id = id;
    }

    public int Id { get; }

    public bool Enabled { get; set; } = true;

    public bool Visible { get; set; } = true;

    public virtual IReadOnlyList<DbusMenuNode> Children => [];
}

internal sealed class DbusMenuItem : DbusMenuNode
{
    private readonly List<DbusMenuNode> _children = [];

    public DbusMenuItem(
        int id,
        string label,
        Func<Task>? onClick = null,
        IEnumerable<DbusMenuNode>? children = null
    )
        : base(id)
    {
        Label = label;
        OnClick = onClick;

        if (children is not null)
        {
            _children.AddRange(children);
        }
    }

    public string Label { get; set; }

    public Func<Task>? OnClick { get; set; }

    public override IReadOnlyList<DbusMenuNode> Children => _children;
}

internal sealed class DbusSeparator : DbusMenuNode
{
    public DbusSeparator(int id)
        : base(id)
    {
    }
}