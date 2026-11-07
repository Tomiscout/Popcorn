namespace Popcorn.Server.Models.Requests;

public class ThumbnailRequest
{
    public Guid FileId { get; set; }
    public long TimeSeconds { get; set; }
}