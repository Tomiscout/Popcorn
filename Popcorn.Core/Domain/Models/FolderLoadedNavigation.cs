using System.Text.Json.Serialization;

namespace Popcorn.Domain.Models;
public class FolderLoadedNavigation
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>Folder row (or virtual source folder) id: the browser player opens folders by id.</summary>
    [JsonPropertyName("fileId")]
    public Guid? FileId { get; set; }
}
