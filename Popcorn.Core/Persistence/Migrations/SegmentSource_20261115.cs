using FluentMigrator;

namespace Popcorn.Persistence.Migrations;

[Migration(20261115)]
public class SegmentSource_20261115 : Migration
{
    public override void Up()
    {
        // Where a reference's audio was taken from, so its range can be edited (null for older references and
        // chapter references: those can only be trimmed within their clip).
        Alter.Table("SegmentInfo").AddColumn("source_file_id").AsString().Nullable();
        Alter.Table("SegmentInfo").AddColumn("source_start").AsDouble().Nullable();
    }

    public override void Down()
    {
        Delete.Column("source_file_id").FromTable("SegmentInfo");
        Delete.Column("source_start").FromTable("SegmentInfo");
    }
}
