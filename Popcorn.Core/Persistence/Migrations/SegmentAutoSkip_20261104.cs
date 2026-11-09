using FluentMigrator;

namespace Popcorn.Persistence.Migrations;

[Migration(20261104)]
public class SegmentAutoSkip_20261104 : Migration
{
    public override void Up()
    {
        // Per-item override of SettingsModel.SegmentAutoSkip (null = global setting).
        Alter.Table("MediaItem").AddColumn("segment_auto_skip").AsInt16().Nullable();
        Alter.Table("SegmentInfo").AddColumn("seen").AsBoolean().NotNullable().WithDefaultValue(false);
    }

    public override void Down()
    {
        Delete.Column("segment_auto_skip").FromTable("MediaItem");
        Delete.Column("seen").FromTable("SegmentInfo");
    }
}
