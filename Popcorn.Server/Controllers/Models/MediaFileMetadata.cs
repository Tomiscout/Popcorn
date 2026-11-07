using Popcorn.Domain.Models;
using Popcorn.Shared.Ffmpeg;

namespace Popcorn.Server.Controllers.Models;

public class MediaFileMetadata
{
    public Guid FileId { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string FullTitle { get; set; } = string.Empty;
    public string ShortTitle { get; set; } = string.Empty;
    public int? Season { get; set; }
    public int? Episode { get; set; }
    public List<AudioTrackDto> AudioTracks { get; set; } = [];
    public List<SubtitleTrackDto> SubtitleTracks { get; set; } = [];
    public VideoCodec? VideoDefaultCodec { get; set; }
    public int? AudioDefaultIndex { get; set; }
    public int? SubtitleDefaultIndex { get; set; }
    public List<string> FormatNames { get; set; } = [];
    /// <summary>Intro/outro locations (outros have a negative start, relative to the end of the file) + auto-skip settings.</summary>
    public PlayerSegments? Segments { get; set; }
    /// <summary>Chapters stored in the file.</summary>
    public List<ChapterInfo> Chapters { get; set; } = [];
    /// <summary>Technical details for the player's Media info tab.</summary>
    public MediaInfo? MediaInfo { get; set; }
    /// <summary>Saved position (seconds) the player starts at, null = from the beginning.</summary>
    public double? ResumePosition { get; set; }
    /// <summary>What the next button plays (shown when hovering it); null outside the library.</summary>
    public NextFilePlan? NextFile { get; set; }

    public AudioTrackDto? GetDefaultAudioTrack() => !AudioDefaultIndex.HasValue ? null : AudioTracks.First(t => t.Index == AudioDefaultIndex.Value);
    public SubtitleTrackDto? GetDefaultSubtitleTrack() => !SubtitleDefaultIndex.HasValue ? null : SubtitleTracks.First(t => t.Index == SubtitleDefaultIndex.Value);
}

public class AudioTrackDto
{
    public required int Index { get; set; }
    public required string Label { get; set; }
    public string? Language { get; set; }
    public required string Codec { get; set; }
    
    public required string Container { get; set; }
}

public class SubtitleTrackDto
{
    public required int Index { get; set; }
    public required string Label { get; set; }
    public string? Language { get; set; }
    public required string Format { get; set; }
}

public enum SubtitleFormat
{
    Unknown = 0,
    Srt,
    Ass,
    Vtt,
    Pgs
}