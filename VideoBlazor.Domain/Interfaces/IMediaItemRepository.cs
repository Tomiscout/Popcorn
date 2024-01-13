using VideoBlazor.Domain.Models;

namespace VideoBlazor.Domain.Interfaces;
public interface IMediaItemRepository
{
    Task<MediaItem> LoadMediaItem(Guid id);
    Task<MediaItem> LoadMediaItem(string id);
    Task SaveMediaItem(MediaItem item);
    Task<List<MediaItem>> LoadMediaItems();
    Task AddMediaItem(MediaItem mediaItem);
    Task<List<ItemHistory>> AddHistoryItem(Guid id, ItemHistory item);
    Task DeleteMediaItem(Guid id);
    Task CleanHistory(MediaItem item);
    Task<List<SegmentInfo>> AddSegmentInfo(Guid id, List<SegmentInfo> segments);
    Task RemoveSegmentInfo(Guid id, Guid segmentId);
    Task ClearSegmentInfo(Guid id);
}
