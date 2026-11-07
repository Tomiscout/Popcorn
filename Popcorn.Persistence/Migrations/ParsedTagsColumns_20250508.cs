using FluentMigrator;

namespace Popcorn.Persistence.Migrations;

[Migration(20250508)]
public class ParsedTagsColumns_20250508 : Migration
{
    public override void Up()
    {
        Alter.Table("MediaFile")
            .AddColumn("tags_processed").AsBoolean().WithDefaultValue(false)
            .AddColumn("season").AsString().Nullable()
            .AddColumn("episode").AsString().Nullable()
            .AddColumn("format_tags").AsString().Nullable()
            .AddColumn("other_tags").AsString().Nullable();
    }

    public override void Down()
    {
        Delete.Column("tags_processed").FromTable("MediaFile");
        Delete.Column("season").FromTable("MediaFile");
        Delete.Column("episode").FromTable("MediaFile");
        Delete.Column("format_tags").FromTable("MediaFile");
        Delete.Column("other_tags").FromTable("MediaFile");
    }
}