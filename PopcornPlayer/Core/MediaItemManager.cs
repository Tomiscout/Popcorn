using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Popcorn.Domain.Models;
using PopcornPlayer.Contracts;
using PopcornPlayer.Domain.Extensions;

namespace PopcornPlayer.Core;
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
        PlayerCore.PostEvent("folder_loaded", @event);
    }
}
