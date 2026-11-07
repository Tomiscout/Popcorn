using System.Text.Json.Serialization;

namespace Popcorn.Domain.Models;
public class FolderLoadedNavigation
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;
}
