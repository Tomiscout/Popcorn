using System.Collections.Generic;
using System.Text.Json.Serialization;
using VideoBlazor.Domain.Models;

namespace VideoPlayer.Contracts;
internal class FolderLoadedEvent
{
    [JsonPropertyName("navigation")]
    public IList<FolderLoadedNavigation> Navigation { get; set; } = new List<FolderLoadedNavigation>();

    [JsonPropertyName("files")]
    public IList<MediaFile> Files { get; set; } = new List<MediaFile>();
}
