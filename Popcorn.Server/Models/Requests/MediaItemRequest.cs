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

/// <summary>Deletes a history entry of an item (<see cref="HistoryId"/> = <c>ItemHistory.Id</c>), or all of them when null.</summary>
public class DeleteMediaItemHistoryRequest
{
    public Guid ItemId { get; set; }
    public long? HistoryId { get; set; }
}

/// <summary>Renames a library item (item page title).</summary>
public class RenameMediaItemRequest
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
}

/// <summary>Removes an item from the library; <see cref="DeleteFiles"/> also deletes its downloads and files from disk.</summary>
public class RemoveMediaItemRequest
{
    public Guid Id { get; set; }
    public bool DeleteFiles { get; set; }
}

/// <summary>Ranks the library items for a release / file name (the add download dialog's preselected item).</summary>
public class MediaItemMatchRequest
{
    public string Name { get; set; } = string.Empty;
}

/// <param name="Score">0..1, title tokens in common (<c>MediaItemMatcher</c>).</param>
public record MediaItemMatchDto(Guid Id, string Title, string? ImagePath, double Score);

/// <summary>Answer to <see cref="SocketRequests.MediaItemMatch"/>: every item, best match first.</summary>
public record MediaItemMatchResponse(string Name, List<MediaItemMatchDto> Items);

/// <summary>Merges <see cref="OtherId"/> into <see cref="TargetId"/> (the other item is deleted, its files kept).</summary>
public class MediaItemMergeRequest
{
    public Guid TargetId { get; set; }
    public Guid OtherId { get; set; }
}

/// <summary>
/// Removes <see cref="SourceId"/> from item <see cref="ItemId"/> (not its last source); <see cref="DeleteFiles"/> also
/// removes its downloads and deletes its files from disk, else its downloads go on outside the library.
/// </summary>
public class MediaItemSourceRemoveRequest
{
    public Guid ItemId { get; set; }
    public Guid SourceId { get; set; }
    public bool DeleteFiles { get; set; }
}
