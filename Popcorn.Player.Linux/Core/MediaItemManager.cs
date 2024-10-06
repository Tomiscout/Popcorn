using Microsoft.Extensions.DependencyInjection;
using Popcorn.Domain.Models;
using Popcorn.Player.Linux.Contracts;
using Popcorn.Player.Linux.Player;
using Popcorn.Player.Linux.WebSocket;
using PopcornPlayer.Domain.Extensions;

namespace Popcorn.Player.Linux.Core;
public class MediaItemManager
{
    private readonly IPlayer _player;
    private readonly Lazy<PlayerWebSocket> _webSocket;
    public MediaItem CurrentMediaItem { get; private set; }

    public MediaItemManager(IPlayer player, Lazy<PlayerWebSocket> webSocket)
    {
        _player = player;
        _webSocket = webSocket;
    }

    public void UpdateItemHistory(List<ItemHistory> history) //From main client
    {
        CurrentMediaItem.History = history;
        var lastPlayed = CurrentMediaItem.History.LastOrDefault();
        CurrentMediaItem.RecalculatePlayedFiles(CurrentMediaItem.Files.Where(f => f.Path == lastPlayed.Filename).ToList());
    }

    public void SetMediaInfo(MediaItem item)
    {
        CurrentMediaItem = item;
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
