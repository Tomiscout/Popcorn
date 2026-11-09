using Popcorn.Domain.Enum;

namespace Popcorn.Domain.Models;

/// <summary>Intro/outro locations of a file as the players need them, with the auto-skip settings that apply.</summary>
public class PlayerSegments
{
    /// <summary>Library item of the file; null = not in the library (no per-item setting).</summary>
    public Guid? ItemId { get; set; }
    public SegmentAutoSkip GlobalMode { get; set; }
    /// <summary>The item's override of <see cref="GlobalMode"/>.</summary>
    public SegmentAutoSkip? ItemMode { get; set; }
    /// <inheritdoc cref="SettingsModel.SkipOutroTail"/>
    public bool SkipOutroTail { get; set; } = true;
    /// <inheritdoc cref="SettingsModel.SkipSegmentsWithoutDelay"/>
    public bool SkipSegmentsWithoutDelay { get; set; }
    public List<PlayerSegment> Segments { get; set; } = [];
}

public class PlayerSegment
{
    /// <summary>The reference (<see cref="SegmentInfo"/>) this location was matched against.</summary>
    public Guid SegmentId { get; set; }
    /// <summary>Outros are negative (relative to the end of the file).</summary>
    public double StartSeconds { get; set; }
    public double Length { get; set; }
    /// <summary>This segment was watched (or skipped) once.</summary>
    public bool Seen { get; set; }
}
