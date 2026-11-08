using FluentMigrator;

namespace Popcorn.Persistence.Migrations;

/// <summary>Where a library item was added from (media source plugin + its media id, IMDb id): <c>MediaSourceRef</c>.</summary>
[Migration(20261108)]
public class MediaItemSource_20261108 : Migration
{
    public override void Up()
    {
        Alter.Table("MediaItem")
            .AddColumn("media_plugin_id").AsString().Nullable()
            .AddColumn("media_id").AsString().Nullable()
            .AddColumn("imdb_id").AsString().Nullable();
    }

    public override void Down()
    {
        Delete.Column("media_plugin_id").Column("media_id").Column("imdb_id").FromTable("MediaItem");
    }
}
