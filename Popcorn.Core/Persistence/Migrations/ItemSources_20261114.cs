using System.Data;
using FluentMigrator;

namespace Popcorn.Persistence.Migrations;

/// <summary>
/// Sources of an item's files (<c>ItemSource</c>) + <c>MediaFile.source_id</c>. Every existing item gets one folder
/// source (its folder) holding all its files; the download manager splits its downloads off on start-up.
/// </summary>
[Migration(20261114)]
public class ItemSources_20261114 : Migration
{
    public override void Up()
    {
        Create.Table("ItemSource")
            .WithColumn("id").AsString().PrimaryKey()
            .WithColumn("item_id").AsString().ForeignKey("MediaItem", "id").OnDeleteOrUpdate(Rule.Cascade)
            .WithColumn("kind").AsInt16()
            .WithColumn("title").AsString()
            .WithColumn("root_path").AsString()
            .WithColumn("added_at").AsDateTime()
            .WithColumn("origins").AsString().Nullable()
            .WithColumn("merged_from_item_id").AsString().Nullable()
            .WithColumn("merged_from_title").AsString().Nullable();

        Create.Index("IX_ItemSource_ItemId").OnTable("ItemSource").OnColumn("item_id").Ascending().WithOptions().NonClustered();

        Alter.Table("MediaFile").AddColumn("source_id").AsString().Nullable();

        Execute.WithConnection((connection, transaction) =>
        {
            var items = new List<(string Id, string Title, string Folder)>();
            using (var select = connection.CreateCommand())
            {
                select.Transaction = transaction;
                select.CommandText = "SELECT id, title, folder_directory FROM MediaItem";
                using var reader = select.ExecuteReader();
                while (reader.Read())
                    items.Add((reader.GetString(0), reader.IsDBNull(1) ? "" : reader.GetString(1), reader.IsDBNull(2) ? "" : reader.GetString(2)));
            }

            foreach (var (itemId, title, folder) in items)
            {
                var sourceId = Guid.NewGuid().ToString();
                Run(connection, transaction,
                    "INSERT INTO ItemSource (id, item_id, kind, title, root_path, added_at) VALUES (@id, @item, 0, @title, @root, @added)",
                    ("@id", sourceId), ("@item", itemId), ("@title", FolderTitle(folder, title)), ("@root", folder), ("@added", DateTime.UtcNow));
                Run(connection, transaction, "UPDATE MediaFile SET source_id = @id WHERE item_id = @item", ("@id", sourceId), ("@item", itemId));
            }
        });
    }

    private static string FolderTitle(string folder, string title)
    {
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(folder));
        return string.IsNullOrWhiteSpace(name) ? title : name;
    }

    private static void Run(IDbConnection connection, IDbTransaction transaction, string sql, params (string Name, object Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }
        command.ExecuteNonQuery();
    }

    public override void Down()
    {
        Delete.Column("source_id").FromTable("MediaFile");
        Delete.Table("ItemSource");
    }
}
