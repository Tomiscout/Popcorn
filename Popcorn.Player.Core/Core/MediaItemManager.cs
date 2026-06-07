using Microsoft.Extensions.DependencyInjection;
using Popcorn.Application.Extensions;
using Popcorn.Domain.Interfaces;
using Popcorn.Domain.Models;
using Popcorn.Player.Core.WebSocket;
using Popcorn.Player.Core.Contracts;
using Popcorn.Player.Core.Player;
using Popcorn.Shared.Utils;

namespace Popcorn.Player.Core.Core;
public class MediaItemManager(
    Lazy<WebEventHandler> webSocket,
    IMediaItemRepository mediaItemRepository,
    IMediaFileRepository mediaFileRepository)
{
    public MediaItem CurrentMediaItem { get; private set; }

    public void UpdateItemHistory(List<ItemHistory> history) //From main client
    {
        CurrentMediaItem.History = history;
        var lastPlayed = CurrentMediaItem.History.LastOrDefault();
        CurrentMediaItem.RecalculatePlayedFiles(CurrentMediaItem.Files.Where(f => f.Id == lastPlayed.FileId).ToList());
    }

    public void LoadMediaInfo(Guid fileId)
    {
        CurrentMediaItem = mediaItemRepository.GetMediaItemFromFileId(fileId).Result;
        CurrentMediaItem.RecalculatePlayedFiles(CurrentMediaItem.Files);

        // Update current item if it already playing
        // if (_player.CurrentMediaFile != null)
        // {
        //     _player.CurrentMediaFile = CurrentMediaItem.Files.First(f => f.Path == PlayerCore.CurrentMediaFile.Path);
        //     AudioAndSubtitles.SendAudioSegments();
        // }
    }

    public void SendCurrentMediaFolderLoaded(string path)
    {
        var @event = new FolderLoadedEvent
        {
            Navigation = CurrentMediaItem.BuildNavigationPath(path),
            Files = CurrentMediaItem.Files.GetFilesForDirectory(path),
        };
        CurrentMediaItem.RecalculatePlayedFiles(@event.Files);
        webSocket.Value.PostEvent("folder_loaded", @event);
    }

//TODO moved to ItemHistoryService
    public async Task UpdatePlayedFile(MediaFile file)
    {
        AssertUtils.AssertGuid(file.Id);
        
        await mediaFileRepository.SetFileAsPlayed(file.Id);
        if (file.ItemId == Guid.Empty) return;
        
        await mediaItemRepository.AddHistoryItem(file.ItemId, new ItemHistory()
        {
            FileId = file.Id,
            ItemId = file.ItemId,
            PlaybackTime = DateTime.UtcNow
        });
            
        // TODO event to popcorn to refresh played items
    }
}
