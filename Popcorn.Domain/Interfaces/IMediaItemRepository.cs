using Popcorn.Domain.Models;

namespace Popcorn.Domain.Interfaces;
public interface IMediaItemRepository
{
    Task<List<MediaItem>> GetMediaItemsWithHistory();
    Task<MediaItem?> GetMediaItem(Guid id);
    Task<MediaItem?> GetMediaItemWithFilesAndHistory(Guid id);
    Task<MediaItem?> GetMediaItemFromFileId(Guid id);
    Task<MediaItem?> GetMediaItemFull(Guid id);
    Task<MediaItem?> GetMediaItemWithSegments(Guid id);
    Task<List<string>> GetMediaItemFolders();
    Task<Guid> CreateMediaItem(MediaItem mediaItem);
    Task DeleteMediaItem(Guid id);
    Task AddHistoryItem(Guid id, ItemHistory item);
    Task<List<ItemHistory>> GetHistoryItems(Guid itemId);
    List<ItemHistoryDto>? GetItemHistoryDtos(MediaItem item);
    Task DeleteHistoryAll(Guid itemId);
    Task AddSegmentLocation(SegmentLocation segmentLocation);
    Task DeleteAllSegments(Guid id);
    Task AddSegmentInfo(Guid itemId, SegmentInfo segment);
    Task DeleteSegmentInfo(Guid segmentId);
}
