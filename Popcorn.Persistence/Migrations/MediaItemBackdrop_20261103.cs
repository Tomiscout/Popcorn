using FluentMigrator;

namespace Popcorn.Persistence.Migrations;

[Migration(20261103)]
public class MediaItemBackdrop_20261103 : Migration
{
    public override void Up()
    {
        Alter.Table("MediaItem").AddColumn("backdrop_path").AsString().Nullable();
    }

    public override void Down()
    {
        Delete.Column("backdrop_path").FromTable("MediaItem");
    }
}
