using FluentMigrator;

namespace Popcorn.Persistence.Migrations;

[Migration(20261105)]
public class ReparseFileTags_20261105 : Migration
{
    public override void Up()
    {
        // The previous tag parser marked files as processed without season / episode numbers (MediaFileTagExtensions
        // stores an empty string when there are none, never NULL). Clearing the flag makes them parse again on use.
        Execute.Sql("UPDATE MediaFile SET tags_processed = 0 WHERE type = 0 AND tags_processed = 1 AND season IS NULL");
    }

    public override void Down()
    {
    }
}
