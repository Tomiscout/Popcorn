using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Dapper.FluentMap.Mapping;
using Popcorn.Domain.Models;

namespace Popcorn.Persistence.Dapper;

public class SegmentInfoMap : EntityMap<SegmentInfo>
{
    public SegmentInfoMap()
    {
        Map(m => m.Id).ToColumn("segment_id");
        Map(m => m.ItemId).ToColumn("item_id");
        Map(m => m.FilePath).ToColumn("file_path");
        Map(m => m.Length).ToColumn("length");
        Map(m => m.Type).ToColumn("type");
        Map(m => m.Seen).ToColumn("seen");
        Map(m => m.SourceFileId).ToColumn("source_file_id");
        Map(m => m.SourceStart).ToColumn("source_start");
    }
}
