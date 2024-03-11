using System.Data;
using System.Data.SQLite;
using Dapper;
using Dapper.FluentMap;
using Popcorn.Persistence.Dapper;
using Popcorn.Shared.Utils;

namespace Popcorn.Persistence;

public class DapperContext
{
    public static string ConnectionString => $"Data Source={Path.Join(DirUtils.DataFolder, "data.db")};Version=3;foreign keys=true;";

    public DapperContext()
    {
        SqlMapper.AddTypeHandler(new GuidTypeHandler());
    }

    public IDbConnection CreateConnection()
       => new SQLiteConnection(ConnectionString);

    public static void RegisterMappings()
    {
        FluentMapper.Initialize(config =>
        {
            config.AddMap(new ItemHistoryMap());
            config.AddMap(new MediaFileMap());
            config.AddMap(new MediaItemMap());
            config.AddMap(new SegmentInfoMap());
            config.AddMap(new SegmentLocationMap());
        });
    }
}
