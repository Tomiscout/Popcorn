
using VideoPlayer.Shared.PipeEvents;

namespace VideoPlayer.Shared;

public interface IPipeClient
{
    public void LoadMedia(LoadMediaEvent loadEvent);
    public void MediaInfo(MediaInfoEvent infoEvent);
    public void Kill();
}
