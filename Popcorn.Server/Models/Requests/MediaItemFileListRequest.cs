namespace Popcorn.Server.Models.Requests;

public class MediaItemFileListRequest : MediaItemRequest
{
    public string Directory { get; set; } = string.Empty;
}