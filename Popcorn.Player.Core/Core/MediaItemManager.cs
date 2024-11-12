using Microsoft.Extensions.DependencyInjection;
using Popcorn.Domain.Interfaces;
using Popcorn.Domain.Models;
using Popcorn.Player.Core.WebSocket;
using Popcorn.Player.Core.Contracts;
using Popcorn.Player.Core.Player;
using PopcornPlayer.Domain.Extensions;

namespace Popcorn.Player.Core.Core;
public class MediaItemManager
{
    private readonly IPlayer _player;
    private readonly Lazy<WebEventHandler> _webSocket;
    private readonly IMediaItemRepository _mediaItemRepository;
    public MediaItem CurrentMediaItem { get; private set; }

    public MediaItemManager(IPlayer player, Lazy<WebEventHandler> webSocket, IMediaItemRepository mediaItemRepository)
    {
        _player = player;
        _webSocket = webSocket;
        _mediaItemRepository = mediaItemRepository;
    }

    public void UpdateItemHistory(List<ItemHistory> history) //From main client
    {
        CurrentMediaItem.History = history;
        var lastPlayed = CurrentMediaItem.History.LastOrDefault();
        CurrentMediaItem.RecalculatePlayedFiles(CurrentMediaItem.Files.Where(f => f.Id == lastPlayed.FileId).ToList());
    }

    public void LoadMediaInfo(Guid fileId)
    {
        CurrentMediaItem = _mediaItemRepository.GetMediaItemFromFileId(fileId).Result;
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
        _webSocket.Value.PostEvent("folder_loaded", @event);
    }
}
