using Popcorn.Domain.Enum;
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
    Task SetImagePath(Guid id, MediaImageKind kind, string? path);
    /// <summary>Renames the item: title and short title (what the UI shows) both become <paramref name="title"/>.</summary>
    Task SetTitle(Guid id, string title);
    Task AddHistoryItem(Guid id, ItemHistory item);
    Task<List<ItemHistory>> GetHistoryItems(Guid itemId);
    List<ItemHistoryDto>? GetItemHistoryDtos(MediaItem item);
    /// <summary>Deletes one history entry (<see cref="ItemHistory.Id"/>) of the item; files left without entries become unplayed.</summary>
    Task DeleteHistoryItem(Guid itemId, long historyId);
    /// <summary>Deletes the item's whole history; its files become unplayed.</summary>
    Task DeleteHistoryAll(Guid itemId);
    /// <summary>Sets the segments of a file: its old locations are replaced by <paramref name="locations"/>.</summary>
    Task ReplaceSegmentLocations(Guid fileId, IReadOnlyCollection<SegmentLocation> locations);
    Task DeleteAllSegments(Guid id);
    Task AddSegmentInfo(Guid itemId, SegmentInfo segment);
    Task DeleteSegmentInfo(Guid segmentId);
    /// <summary>Changes a reference's type and drops its locations (their start sign is the old type's).</summary>
    Task SetSegmentType(Guid segmentId, SegmentType type);
    /// <summary>Marks files of the item that have no location of <paramref name="type"/> as not scanned for it.</summary>
    Task ResetSegmentsProcessed(Guid itemId, SegmentType type);
    /// <summary>Locations of a file with their references' seen state (<see cref="PlayerSegments"/> fields only).</summary>
    Task<List<PlayerSegment>> GetPlayerSegments(Guid fileId);
    Task SetSegmentSeen(Guid segmentId);
    Task<SegmentAutoSkip?> GetSegmentAutoSkip(Guid itemId);
    Task SetSegmentAutoSkip(Guid itemId, SegmentAutoSkip? mode);
}
