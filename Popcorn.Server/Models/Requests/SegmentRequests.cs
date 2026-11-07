using Popcorn.Domain.Enum;
using Popcorn.Domain.Models;
using Popcorn.SegmentFinder;

namespace Popcorn.Server.Models.Requests;

public class SegmentsDetectRequest
{
    public Guid ItemId { get; set; }
    public List<Guid> FileIds { get; set; } = [];
}

public class SegmentsSetTypeRequest
{
    public Guid ItemId { get; set; }
    public Guid SegmentId { get; set; }
    public SegmentType Type { get; set; }
}

public class SegmentsDeleteRequest
{
    public Guid ItemId { get; set; }
    public Guid SegmentId { get; set; }
}

/// <summary>Per file id the segments it should have (replacing its saved ones); files left out are not touched.</summary>
public class SegmentsApplyRequest
{
    public Guid ItemId { get; set; }
    public Dictionary<Guid, List<SegmentPick>> Files { get; set; } = [];
}

public class SegmentAutoSkipSetRequest
{
    public Guid ItemId { get; set; }
    /// <summary>Null = use the global setting.</summary>
    public SegmentAutoSkip? Mode { get; set; }
}
