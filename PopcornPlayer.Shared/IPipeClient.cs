
using Popcorn.Domain.Models;
using PopcornPlayer.Shared.PipeEvents.Client;

namespace PopcornPlayer.Shared;

public interface IPipeClient
{
    public void LoadMedia(LoadMediaEventWindows loadEvent);
    public void MediaInfo(MediaItem mediaItem);
    public void UpdateMediaItemHistory(List<ItemHistory> history);
    public void Kill();
}
