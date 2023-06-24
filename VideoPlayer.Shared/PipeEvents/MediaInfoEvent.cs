
using VideoBlazor.Domain.Models;

namespace VideoPlayer.Shared.PipeEvents;

public class MediaInfoEvent : PipeEvent
{
    public IList<MediaFile> Folders { get; set; }
}
