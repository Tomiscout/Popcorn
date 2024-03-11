
namespace Popcorn.Domain.Models;

public class ItemHistory
{
    public Guid ItemId { get; set; }
    public Guid FileId { get; set; }

    [Obsolete]
    public string Filename { get; set; }
    public string Title { get; set; }
    public DateTime PlaybackTime { get; set; }
}
