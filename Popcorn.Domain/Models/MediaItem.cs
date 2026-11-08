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
    /// <summary>Media source plugin the item was added from (its details page), see <see cref="MediaSourceRef"/>.</summary>
    public string? MediaPluginId { get; set; }
    /// <summary>That plugin's id of the media.</summary>
    public string? MediaId { get; set; }
    /// <summary>IMDb id ("tt1234567") of the media, when known.</summary>
    public string? ImdbId { get; set; }
    public List<MediaFile> Files { get;set; }
    public List<ItemHistory> History { get;set; }

    public List<SegmentInfo> Segments { get; set; }

    public MediaSourceRef? Source
    {
        get => MediaSourceRef.Clean(new MediaSourceRef { PluginId = MediaPluginId, MediaId = MediaId, ImdbId = ImdbId });
        set
        {
            var source = MediaSourceRef.Clean(value);
            MediaPluginId = source?.PluginId;
            MediaId = source?.MediaId;
            ImdbId = source?.ImdbId;
        }
    }

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
