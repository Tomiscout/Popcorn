namespace Popcorn.Server.Models.Requests;

public class FileOpenWithRequest
{
    /// <summary>File to open. When null, the next unwatched file of <see cref="MediaId"/> is opened.</summary>
    public Guid? FileId { get; set; }
    public Guid? MediaId { get; set; }
    /// <summary><see cref="Popcorn.Domain.Models.PlayerOption.Id"/> of the player to use.</summary>
    public string PlayerId { get; set; } = string.Empty;
}
