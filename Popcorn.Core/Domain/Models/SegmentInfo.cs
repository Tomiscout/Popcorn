
namespace Popcorn.Domain.Models;
public class SegmentInfo
{
    public Guid Id { get; set; }
    public Guid ItemId { get; set; }
    public string FilePath { get; set; }
    public double Length { get; set; }
    public SegmentType Type { get; set; }
    /// <summary>Watched (or skipped) once in a player; drives <see cref="Popcorn.Domain.Enum.SegmentAutoSkip"/>.</summary>
    public bool Seen { get; set; }
    /// <summary>
    /// Media file the audio was taken from and where in it (seconds), so the range can be edited. Null for references
    /// saved before this was stored and for chapter references.
    /// </summary>
    public Guid? SourceFileId { get; set; }
    public double? SourceStart { get; set; }
}

public enum SegmentType
{
    Intro,
    Outro
}
