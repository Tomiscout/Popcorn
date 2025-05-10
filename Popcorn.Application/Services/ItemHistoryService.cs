using Popcorn.Domain.Interfaces;
using Popcorn.Domain.Models;
using Popcorn.Persistence.Extensions;

namespace Popcorn.Application.Services;

public class ItemHistoryService(IMediaItemRepository mediaItemRepository, IMediaFileRepository mediaFileRepository)
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
                Title = file?.ShortTitle ?? file?.Title ?? string.Empty,
                PlaybackTime = h.PlaybackTime,
                FileId = h.FileId,
            };
        })
        .ToList();
    }
}