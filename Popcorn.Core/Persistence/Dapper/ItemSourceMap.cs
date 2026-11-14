using Dapper.FluentMap.Mapping;
using Popcorn.Domain.Models;

namespace Popcorn.Persistence.Dapper;

public class ItemSourceMap : EntityMap<ItemSource>
{
    public ItemSourceMap()
    {
        Map(m => m.Id).ToColumn("id");
        Map(m => m.ItemId).ToColumn("item_id");
        Map(m => m.Kind).ToColumn("kind");
        Map(m => m.Title).ToColumn("title");
        Map(m => m.RootPath).ToColumn("root_path");
        Map(m => m.AddedAt).ToColumn("added_at");
        Map(m => m.OriginsJson).ToColumn("origins");
        Map(m => m.Origins).Ignore();
        Map(m => m.MergedFromItemId).ToColumn("merged_from_item_id");
        Map(m => m.MergedFromTitle).ToColumn("merged_from_title");
    }
}
