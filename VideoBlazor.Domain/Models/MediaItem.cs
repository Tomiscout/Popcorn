using VideoBlazor.Domain.Models;

namespace VideoBlazor.Domain.Models;

public class MediaItem
{
    public Guid Id { get; set; }
    public string Title { get; set; }
    public string Image { get; set; }
    public string ImagePath { get; set; }
    public string FolderDirectory { get; set; }
    public IList<MediaFile> Files { get;set; }
    public IList<PlaybackHistory> History { get;set; }
}
