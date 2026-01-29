using Popcorn.Domain.Models;

namespace Popcorn.Server.Models.Responses;

public class PlayerPlaylistResponse
{
    public List<FolderLoadedNavigation> Navigation { get; set; } = [];
    public List<MediaFile> Files { get; set; } = [];
}