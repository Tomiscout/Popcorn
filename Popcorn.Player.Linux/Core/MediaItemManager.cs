using Popcorn.Domain.Models;
using Popcorn.Player.Linux.Contracts;
using Popcorn.Player.Linux.WebSocket;
using PopcornPlayer.Domain.Extensions;

namespace Popcorn.Player.Linux.Core;
public static class MediaItemManager
{
    public static MediaItem CurrentMediaItem = null;

    public static void UpdateItemHistory(List<ItemHistory> history) //From main client
    {
        CurrentMediaItem.History = history;
        var lastPlayed = CurrentMediaItem.History.LastOrDefault();
        CurrentMediaItem.RecalculatePlayedFiles(CurrentMediaItem.Files.Where(f => f.Path == lastPlayed.Filename).ToList());
    }

    public static void SetMediaInfo(MediaItem item)
    {
        CurrentMediaItem = item;
        CurrentMediaItem.RecalculatePlayedFiles(CurrentMediaItem.Files);

        // Update current item if it already playing
        if (PlayerCore.CurrentMediaFile != null)
        {
            PlayerCore.CurrentMediaFile = CurrentMediaItem.Files.First(f => f.Path == PlayerCore.CurrentMediaFile.Path);
            AudioAndSubtitles.SendAudioSegments();
        }
    }

    public static void SendCurrentMediaFolderLoaded(string path)
    {
        var @event = new FolderLoadedEvent
        {
            Navigation = CurrentMediaItem.BuildNavigationPath(path),
            Files = CurrentMediaItem.Files.GetFilesForDirectory(path),
        };
        CurrentMediaItem.RecalculatePlayedFiles(@event.Files);
        PlayerWebSocket.PostEvent("folder_loaded", @event);
    }
}
