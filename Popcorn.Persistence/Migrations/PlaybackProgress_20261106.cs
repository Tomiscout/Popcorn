using FluentMigrator;

namespace Popcorn.Persistence.Migrations;

[Migration(20261106)]
public class PlaybackProgress_20261106 : Migration
{
    public override void Up()
    {
        // Where the Popcorn players last were in a file (seconds); null = never reported (external players, old rows).
        Alter.Table("MediaFile").AddColumn("playback_position").AsDouble().Nullable();
        Alter.Table("MediaFile").AddColumn("duration").AsDouble().Nullable();
    }

    public override void Down()
    {
        Delete.Column("playback_position").FromTable("MediaFile");
        Delete.Column("duration").FromTable("MediaFile");
    }
}
