using System.Text.Json.Serialization;
using Popcorn.Domain.Models;

namespace Popcorn.Player.Core.Contracts;
internal class FolderLoadedEvent
{
    [JsonPropertyName("navigation")]
    public IList<FolderLoadedNavigation> Navigation { get; set; } = new List<FolderLoadedNavigation>();

    [JsonPropertyName("files")]
    public IList<MediaFile> Files { get; set; } = new List<MediaFile>();
}
