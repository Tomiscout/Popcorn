
namespace Popcorn.Domain.Models;

public class ItemHistory
{
    public Guid ItemId { get; set; }
    public Guid FileId { get; set; }
    public DateTime PlaybackTime { get; set; }
}
