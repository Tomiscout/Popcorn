using Microsoft.Extensions.Logging;
using Popcorn.Domain.Interfaces;
using Popcorn.Domain.Models;
using Popcorn.Persistence.Extensions;

namespace Popcorn.Application.Services;

public class ItemHistoryService(IMediaItemRepository mediaItemRepository, IMediaFileRepository mediaFileRepository, ILogger<ItemHistoryService> logger)
{
    public async Task<List<ItemHistoryDto>> GetItemHistoryAsync(Guid itemId, List<ItemHistory>? historyItems = null)
    {
        historyItems ??= await mediaItemRepository.GetHistoryItems(itemId);
        
        var files = await mediaFileRepository.GetFilesByIds(
            historyItems.Select(x => x.FileId.SqliteId()).ToArray());
        
        return historyItems.Select(h =>
        {
            var file = files.FirstOrDefault(f => f.Id == h.FileId);
            return new ItemHistoryDto
            {
                Id = h.Id,
                Title = file?.ShortTitle ?? file?.Title ?? string.Empty,
                PlaybackTime = h.PlaybackTime,
                FileId = h.FileId,
            };
        })
        .ToList();
    }

    public async Task ItemWasPlayedAsync(Guid fileId)
    {
        var file = await mediaFileRepository.GetFileById(fileId);
        if (file == null)
        {
            logger.LogError($"File {fileId} not found when setting item history");
            return;
        }
        
        await mediaFileRepository.SetFileAsPlayed(fileId);
        await mediaItemRepository.AddHistoryItem(file.ItemId, new ItemHistory()
        {
            FileId = fileId,
            ItemId = file.ItemId,
            PlaybackTime = DateTime.UtcNow
        });
    }
}