using Popcorn.Domain.Models;

namespace Popcorn.Server.Models.Responses;

/// <summary>Item page's flattened file list: every file of the item in play order (<c>MediaItemExtensions.GetPlayOrder</c>).</summary>
public class MediaItemFlatFileListResponse
{
    public Guid ItemId { get; set; }
    public List<MediaFile> Files { get; set; } = [];
    /// <summary>More than one download feeds the item: the page shows the flattened list unless the user switched it off.</summary>
    public bool MultipleDownloads { get; set; }
}
