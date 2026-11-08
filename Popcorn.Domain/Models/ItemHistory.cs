
namespace Popcorn.Domain.Models;

public class ItemHistory
{
    /// <summary>SQLite rowid of the entry (the table has no key column); 0 when not read from the database.</summary>
    public long Id { get; set; }
    public Guid ItemId { get; set; }
    public Guid FileId { get; set; }
    public DateTime PlaybackTime { get; set; }
}
