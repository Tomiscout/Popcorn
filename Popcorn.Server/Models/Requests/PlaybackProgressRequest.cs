namespace Popcorn.Server.Models.Requests;

/// <summary>Web player position in a file, seconds.</summary>
public class PlaybackProgressRequest
{
    public Guid FileId { get; set; }
    public double Position { get; set; }
    public double Duration { get; set; }
}
