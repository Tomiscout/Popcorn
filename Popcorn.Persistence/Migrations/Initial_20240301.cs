using FluentMigrator;

namespace Popcorn.Persistence.Migrations;


[Migration(20240301)]
public class Initial_20240301 : Migration
{
    public override void Up()
    {
        Create.Table("MediaItem")
            .WithColumn("id").AsString().PrimaryKey()
            .WithColumn("title").AsString()
            .WithColumn("short_title").AsString().Nullable()
            .WithColumn("image_path").AsString().Nullable()
            .WithColumn("folder_directory").AsString();

        Create.Table("SegmentInfo")
            .WithColumn("segment_id").AsString().PrimaryKey()
            .WithColumn("item_id").AsString().ForeignKey("MediaItem", "id").OnDeleteOrUpdate(System.Data.Rule.Cascade)
            .WithColumn("file_path").AsString()
            .WithColumn("length").AsDouble()
            .WithColumn("type").AsInt16();

        Create.Table("SegmentLocation")
            .WithColumn("segment_id").AsString().ForeignKey("SegmentInfo", "segment_id").OnDeleteOrUpdate(System.Data.Rule.Cascade)
            .WithColumn("file_id").AsString().ForeignKey("MediaFile", "id").OnDeleteOrUpdate(System.Data.Rule.Cascade)
            .WithColumn("start_seconds").AsDouble()
            .WithColumn("length").AsDouble();

        Create.Index("IX_SegmentLocation_FileId")
            .OnTable("SegmentLocation")
            .OnColumn("file_id").Ascending()
            .WithOptions().NonClustered();

        Create.Table("ItemHistory")
            .WithColumn("item_id").AsString().ForeignKey("MediaItem", "id").OnDeleteOrUpdate(System.Data.Rule.Cascade)
            .WithColumn("file_id").AsString().ForeignKey("MediaFile", "id").OnDeleteOrUpdate(System.Data.Rule.Cascade)
            .WithColumn("playback_time").AsDateTime();

        Create.Index("IX_ItemHistory_ItemId")
            .OnTable("ItemHistory")
            .OnColumn("item_id").Ascending()
            .WithOptions().NonClustered();

        Create.Table("MediaFile")
            .WithColumn("id").AsString().PrimaryKey()
            .WithColumn("item_id").AsString().ForeignKey("MediaItem", "id").OnDeleteOrUpdate(System.Data.Rule.Cascade)
            .WithColumn("file_path").AsString()
            .WithColumn("title").AsString()
            .WithColumn("short_title").AsString().Nullable()
            .WithColumn("type").AsInt16()
            .WithColumn("played").AsBoolean()
            .WithColumn("intro_processed").AsBoolean()
            .WithColumn("outro_processed").AsBoolean();

        Create.Index("IX_MediaFile_ItemId")
            .OnTable("MediaFile")
            .OnColumn("item_id").Ascending()
            .WithOptions().NonClustered();
    }

    public override void Down()
    {
        Delete.Table("MediaFile");
        Delete.Table("ItemHistory");
        Delete.Table("SegmentInfo");
        Delete.Table("SegmentLocation");
        Delete.Table("MediaItem");
    }
}
