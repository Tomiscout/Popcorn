using Popcorn.Domain.Interfaces;
using Popcorn.Domain.Services;
using PopcornPlayer.Shared;
using PopcornPlayer.Shared.PipeEvents.Server;

namespace Popcorn.PlayerServer.Windows;

public class PipeServer : IPipeServer
{
    private readonly IMediaItemRepository _mediaItemRepository;
    private readonly MediaItemRefreshService _mediaItemRefreshService;

    public PipeServer(IMediaItemRepository mediaItemRepository, MediaItemRefreshService mediaItemRefreshService)
    {
        _mediaItemRepository = mediaItemRepository;
        _mediaItemRefreshService = mediaItemRefreshService;
    }

    public async Task OnClientInitialized()
    {
    }

    public async Task TrackPlayed(TrackPlayedEvent e)
    {
        await _mediaItemRepository.AddHistoryItem(e.ItemId, e.HistoryItem);
        var newHistory = await _mediaItemRepository.GetHistoryItems(e.ItemId);
        await ServerPipeManager.UpdateMediaItemHistory(newHistory);
        _mediaItemRefreshService.RequestRefresh(e.ItemId);
    }
}
