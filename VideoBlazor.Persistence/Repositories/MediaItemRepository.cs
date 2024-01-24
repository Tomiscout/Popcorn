using VideoBlazor.Domain.Interfaces;
using VideoBlazor.Domain.Models;

namespace VideoBlazor.Persistence.Repositories;
public class MediaItemRepository : IMediaItemRepository
{
    private static readonly string _jsonFormat = "mediaItems-{0}.json";

    private readonly JsonRepository _jsonRepository;
    private readonly ImageRepository _imageRepository;

    public MediaItemRepository(JsonRepository jsonRepository, ImageRepository imageRepository)
    {
        _jsonRepository = jsonRepository;
        _imageRepository = imageRepository;
    }

    public async Task<MediaItem> LoadMediaItem(Guid id)
    {
        return await LoadMediaItem(id.ToString());
    }
    public async Task<MediaItem> LoadMediaItem(string id)
    {
        var mediaPath = string.Format(_jsonFormat, id);
        return await _jsonRepository.LoadJsonAsync<MediaItem>(mediaPath);
    }

    public async Task SaveMediaItem(MediaItem item)
    {
        await _jsonRepository.SaveJsonAsync(item, string.Format(_jsonFormat, item.Id.ToString()));
    }

    public async Task<List<MediaItem>> LoadMediaItems()
    {
        try
        {
            var search = string.Format(_jsonFormat, "*");
            var allPaths = Directory.EnumerateFiles(_jsonRepository.GetDataFolder(), search, SearchOption.TopDirectoryOnly).ToList();
            var result = new List<MediaItem>();
            foreach (var path in allPaths)
            {
                result.Add(await _jsonRepository.LoadJsonAsync<MediaItem>(Path.GetFileName(path)));
            }
            return result;
        }
        catch (FileNotFoundException)
        {
            var emptyList = new List<MediaItem>();
            return emptyList;
        }
    }

    public async Task AddMediaItem(MediaItem mediaItem)
    {
        mediaItem.Id = Guid.NewGuid();
        if (!string.IsNullOrWhiteSpace(mediaItem.ImagePath))
        {
            _imageRepository.SaveImage(mediaItem.ImagePath, mediaItem.Id.ToString());
        }

        await SaveMediaItem(mediaItem);
    }

    public async Task<List<ItemHistory>> AddHistoryItem(Guid id, ItemHistory item)
    {
        var mediaItem = await LoadMediaItem(id);
        if (mediaItem.History is null)
        {
            mediaItem.History = new List<ItemHistory>();
        }
        mediaItem.History.Add(item);
        await SaveMediaItem(mediaItem);
        return mediaItem.History;
    }

    public async Task DeleteMediaItem(Guid id)
    {
        var currentItem = await LoadMediaItem(id);
        if (currentItem is not null)
        {
            await _jsonRepository.DeleteJsonAsync(string.Format(_jsonFormat, id.ToString()));
        }
    }

    public async Task CleanHistory(MediaItem item)
    {
        item.History = null;
        await SaveMediaItem(item);
    }

    public async Task<List<SegmentInfo>> AddSegmentInfo(Guid id, List<SegmentInfo> segments)
    {
        var mediaItem = await LoadMediaItem(id);
        if (mediaItem.Segments is null)
        {
            mediaItem.Segments = segments;
            await SaveMediaItem(mediaItem);
            return segments;
        }

        mediaItem.Segments.AddRange(segments);
        await SaveMediaItem(mediaItem);
        return mediaItem.Segments;
    }

    public async Task RemoveSegmentInfo(Guid id, Guid segmentId)
    {
        var mediaItem = await LoadMediaItem(id);
        mediaItem.Segments = mediaItem.Segments.Where(s => s.Id != segmentId).ToList();
        await SaveMediaItem(mediaItem);
    }

    public async Task ClearSegmentInfo(Guid id)
    {
        var mediaItem = await LoadMediaItem(id);

        foreach(var file in mediaItem.Files)
        {
            file.IntroProcessed = false;
            file.OutroProcessed = false;
            file.IntroTimes = null;
            file.OutroTimes = null;
        }
        await SaveMediaItem(mediaItem);
    }
}
