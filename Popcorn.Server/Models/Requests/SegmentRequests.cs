using Popcorn.Domain.Enum;
using Popcorn.Domain.Models;
using Popcorn.SegmentFinder;

namespace Popcorn.Server.Models.Requests;

public class SegmentsDetectRequest
{
    public Guid ItemId { get; set; }
    public List<Guid> FileIds { get; set; } = [];
}

/// <summary>
/// "Scan all" leaves everything but <see cref="ItemId"/> at its default. "Scan missing" / "Scan selected" name the
/// files and the saved segments to look for.
/// </summary>
public class SegmentsScanRequest
{
    public Guid ItemId { get; set; }
    /// <summary>Null = every file of the item.</summary>
    public List<Guid>? FileIds { get; set; }
    /// <summary>
    /// Saved segments (references) to match; null = all of them plus chapters named intro / outro. With a list only
    /// the types in it are scanned, chapters are not read and files are not marked scanned.
    /// </summary>
    public List<Guid>? SegmentIds { get; set; }
    /// <summary>
    /// What is found replaces the file's saved segments of that type ("Scan selected"); otherwise only types the file
    /// has no saved segment of get the find.
    /// </summary>
    public bool Replace { get; set; }
}

public class SegmentsSetTypeRequest
{
    public Guid ItemId { get; set; }
    public Guid SegmentId { get; set; }
    public SegmentType Type { get; set; }
}

/// <summary>Range editor of a saved segment: answered with <see cref="SegmentEditInfo"/>.</summary>
public class SegmentsEditInfoRequest
{
    public Guid ItemId { get; set; }
    public Guid SegmentId { get; set; }
}

/// <summary>
/// Re-cuts a saved segment's audio; seconds in the editor's audio (<see cref="SegmentEditInfo"/>). Drops it from the
/// files it was applied to (scan again).
/// </summary>
public class SegmentsSetRangeRequest
{
    public Guid ItemId { get; set; }
    public Guid SegmentId { get; set; }
    public double Start { get; set; }
    public double Length { get; set; }
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
