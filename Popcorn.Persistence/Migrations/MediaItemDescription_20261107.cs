using FluentMigrator;

namespace Popcorn.Persistence.Migrations;

[Migration(20261107)]
public class MediaItemDescription_20261107 : Migration
{
    public override void Up()
    {
        Alter.Table("MediaItem").AddColumn("description").AsString().Nullable();
    }

    public override void Down()
    {
        Delete.Column("description").FromTable("MediaItem");
    }
}
