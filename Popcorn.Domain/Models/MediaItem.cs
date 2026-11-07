using Popcorn.Domain.Models;
using Popcorn.Shared.Utils;

namespace Popcorn.Domain.Models;

public class MediaItem
{
    public Guid Id { get; set; }
    public string Title { get; set; }
    public string ShortTitle { get; set; }
    
    [Obsolete("Used in blazor form only")]
    public string? Image { get; set; }
    /// <summary>Poster (portrait) in the images folder.</summary>
    public string? ImagePath { get; set; }
    /// <summary>Wide background picture in the images folder.</summary>
    public string? BackdropPath { get; set; }
    public string FolderDirectory { get; set; }
    /// <summary>Plot summary, from a media source plugin when the item was added.</summary>
    public string? Description { get; set; }
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
