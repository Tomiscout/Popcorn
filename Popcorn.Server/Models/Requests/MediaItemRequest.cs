namespace Popcorn.Server.Models.Requests;

public class MediaItemRequest
{
    public Guid Id { get; set; }
}
/// <summary>Adds a scanned folder to the library. Images: http(s) URL (image provider) or data: URL (upload).</summary>
public class CreateMediaItemRequest
{
    public string FolderPath { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? PosterUrl { get; set; }
    public string? BackdropUrl { get; set; }
}

/// <summary>Replaces an item's poster or backdrop. <see cref="Url"/>: http(s) or data: URL; null/empty removes it.</summary>
public class SetMediaItemImageRequest
{
    public Guid Id { get; set; }
    public Popcorn.Domain.Enum.MediaImageKind Kind { get; set; }
    public string? Url { get; set; }
}

/// <summary>Removes an item from the library; <see cref="DeleteFiles"/> also deletes its downloads and files from disk.</summary>
public class RemoveMediaItemRequest
{
    public Guid Id { get; set; }
    public bool DeleteFiles { get; set; }
}
