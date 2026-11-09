using Dapper.FluentMap.Mapping;
using Popcorn.Domain.Models;

namespace Popcorn.Persistence.Dapper;

public class SegmentLocationMap : EntityMap<SegmentLocation>
{
    public SegmentLocationMap()
    {
        Map(m => m.SegmentId).ToColumn("segment_id");
        Map(m => m.FileId).ToColumn("file_id");
        Map(m => m.StartSeconds).ToColumn("start_seconds");
        Map(m => m.Length).ToColumn("length");
    }
}
