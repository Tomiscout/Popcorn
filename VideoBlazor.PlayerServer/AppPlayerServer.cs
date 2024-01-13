using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VideoBlazor.Domain.Interfaces;
using VideoBlazor.Domain.Models;
using VideoBlazor.Domain.Services;
using VideoPlayer.Shared;
using VideoPlayer.Shared.PipeEvents.Server;

namespace VideoBlazor.PlayerServer
{
    public class PipeServer : IPipeServer
    {
        private MediaItem _initialItem;
        private readonly IMediaItemRepository _mediaItemRepository;
        private readonly MediaItemRefreshService _mediaItemRefreshService;

        public PipeServer(MediaItem initialItem, IMediaItemRepository mediaItemRepository, MediaItemRefreshService mediaItemRefreshService)
        {
            _initialItem = initialItem;
            _mediaItemRepository = mediaItemRepository;
            _mediaItemRefreshService = mediaItemRefreshService;
        }

        public async Task OnClientInitialized() {
            // Hacky way to fix sync problems at start. This wont be a problem at this point
            // TODO maybe this hack is unnecessary, i was testing on broken file
            await ServerPipeManager.SendMediaInfoEvent(_initialItem);
        }

        public async Task TrackPlayed(TrackPlayedEvent e)
        {
            var newHistory = await _mediaItemRepository.AddHistoryItem(e.ItemId, e.HistoryItem);
            await ServerPipeManager.UpdateMediaItemHistory(newHistory);
            _mediaItemRefreshService.RequestRefresh(e.ItemId);
        }
    }
}
