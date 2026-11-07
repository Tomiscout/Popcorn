
namespace Popcorn.Persistence.Extensions;

public static class SqliteExtensions
{
    public static string SqliteId(this Guid? id)
    {
        if (id == null || id == Guid.Empty) return null;
        return id.Value.ToString();
    }

    public static string SqliteId(this Guid id)
    {
        if (id == Guid.Empty) return null;
        return id.ToString();
    }
}
