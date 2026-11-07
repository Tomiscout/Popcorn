using Dapper.FluentMap.Mapping;

namespace Popcorn.Persistence.Dapper;

using Popcorn.Domain.Models;

public class MediaItemMap : EntityMap<MediaItem>
{
    public MediaItemMap()
    {
        Map(m => m.Id).ToColumn("id");
        Map(m => m.Title).ToColumn("title");
        Map(m => m.ShortTitle).ToColumn("short_title");
        Map(m => m.Image).ToColumn("image");
        Map(m => m.ImagePath).ToColumn("image_path");
        Map(m => m.BackdropPath).ToColumn("backdrop_path");
        Map(m => m.FolderDirectory).ToColumn("folder_directory");
        Map(m => m.Description).ToColumn("description");
    }
}