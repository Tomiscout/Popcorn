namespace Popcorn.Server.Models.Requests;

public class MediaSearchRequest
{
    public string SearchText { get; set; } = string.Empty;
    public bool IsTvShow { get; set; }
}
