using Popcorn.Domain.Models;

namespace Popcorn.Server.Models.Responses;

public class MediaListResponse
{
    public List<MediaItem>? RecentItems { get; set; }
    public List<MediaItem>? NewItems { get; set; }
    public List<MediaItem>? RemovedItems { get; set; }
}