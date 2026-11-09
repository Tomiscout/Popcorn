using Popcorn.Domain.Enum;
using Popcorn.Domain.Interfaces;
using Popcorn.Domain.Models;
using Popcorn.Persistence;

namespace Popcorn.Application.Services;

/// <summary>
/// Intro/outro data for the players (server web player and native players): locations of a file, the auto-skip
/// setting that applies (global <see cref="SettingsModel.SegmentAutoSkip"/>, per-item override) and which segments
/// were already seen (the "after first" modes).
/// </summary>
public class SegmentSkipService(IMediaItemRepository mediaItemRepository, SettingsRepository settingsRepository)
{
    public async Task<PlayerSegments> GetAsync(MediaFile file)
    {
        var settings = await settingsRepository.GetSettingsAsync();
        var inLibrary = file.ItemId != Guid.Empty;
        return new PlayerSegments
        {
            ItemId = inLibrary ? file.ItemId : null,
            GlobalMode = settings.SegmentAutoSkip,
            SkipOutroTail = settings.SkipOutroTail,
            SkipSegmentsWithoutDelay = settings.SkipSegmentsWithoutDelay,
            ItemMode = inLibrary ? await mediaItemRepository.GetSegmentAutoSkip(file.ItemId) : null,
            Segments = file.Id == Guid.Empty ? [] : await mediaItemRepository.GetPlayerSegments(file.Id),
        };
    }

    public Task MarkSeenAsync(Guid segmentId) => mediaItemRepository.SetSegmentSeen(segmentId);

    /// <summary><paramref name="mode"/> null = back to the global setting.</summary>
    public Task SetItemModeAsync(Guid itemId, SegmentAutoSkip? mode) => mediaItemRepository.SetSegmentAutoSkip(itemId, mode);
}
