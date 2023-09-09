
using VideoBlazor.Domain.Models;
using VideoPlayer.Shared.PipeEvents.Client;

namespace VideoPlayer.Shared;

public interface IPipeClient
{
    public void LoadMedia(LoadMediaEvent loadEvent);
    public void MediaInfo(MediaItem mediaItem);
    public void Kill();
}
