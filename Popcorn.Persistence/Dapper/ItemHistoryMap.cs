using Dapper.FluentMap.Mapping;
using Popcorn.Domain.Models;

namespace Popcorn.Persistence.Dapper;
public class ItemHistoryMap : EntityMap<ItemHistory>
{
    public ItemHistoryMap()
    {
        Map(m => m.Id).ToColumn("history_id");
        Map(m => m.ItemId).ToColumn("item_id");
        Map(m => m.FileId).ToColumn("file_id");
        Map(m => m.PlaybackTime).ToColumn("playback_time");
    }
}
