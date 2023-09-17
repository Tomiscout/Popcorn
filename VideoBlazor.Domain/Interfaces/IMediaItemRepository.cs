using VideoBlazor.Domain.Models;

namespace VideoBlazor.Domain.Interfaces;
public interface IMediaItemRepository
{
    Task<MediaItem> LoadMediaItem(Guid id);
    Task<MediaItem> LoadMediaItem(string id);
    Task SaveMediaItem(MediaItem item);
    Task<IList<MediaItem>> LoadMediaItems();
    Task AddMediaItem(MediaItem mediaItem);
    Task<IList<PlaybackHistory>> AddHistoryItem(Guid id, PlaybackHistory item);
    Task DeleteMediaItem(Guid id);
    Task CleanHistory(MediaItem item);
}
