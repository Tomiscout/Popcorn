using Dapper.FluentMap.Mapping;
using Popcorn.Domain.Models;

namespace Popcorn.Persistence.Dapper;
public class MediaFileMap : EntityMap<MediaFile>
{
    public MediaFileMap()
    {
        Map(m => m.Id).ToColumn("id");
        Map(m => m.ItemId).ToColumn("item_id");
        Map(m => m.Path).ToColumn("file_path"); 
        Map(m => m.Title).ToColumn("title");
        Map(m => m.ShortTitle).ToColumn("short_title");
        Map(m => m.Type).ToColumn("type");
        Map(m => m.Played).ToColumn("played");
        Map(m => m.IntroProcessed).ToColumn("intro_processed");
        Map(m => m.OutroProcessed).ToColumn("outro_processed");
        Map(m => m.TagsProcessed).ToColumn("tags_processed");
        Map(m => m.Episodes).ToColumn("episode");
        Map(m => m.Seasons).ToColumn("season");
        Map(m => m.FormatTags).ToColumn("format_tags");
        Map(m => m.OtherTags).ToColumn("other_tags");
        Map(m => m.PlaybackPosition).ToColumn("playback_position");
        Map(m => m.Duration).ToColumn("duration");
    }
}
