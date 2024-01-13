using VideoBlazor.Domain.Models;
using VideoBlazor.Shared.Utils;

namespace VideoBlazor.Domain.Models;

public class MediaItem
{
    public Guid Id { get; set; }
    public string Title { get; set; }
    public string ShortTitle { get; set; }
    public string Image { get; set; }
    public string ImagePath { get; set; }
    public string FolderDirectory { get; set; }
    public List<MediaFile> Files { get;set; }
    public List<ItemHistory> History { get;set; }

    public List<SegmentInfo> Segments { get; set; }

    public string GetDisplayTitle()
    {
        if(string.IsNullOrWhiteSpace(ShortTitle))
        {
            var (parsed, _) = StringUtils.SanitizeFilename(Title, new List<string>());
            ShortTitle = parsed;
        }
        return ShortTitle;
    }
}
