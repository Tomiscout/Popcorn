using System.Text.Json.Serialization;
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
    /// <summary>Audio track inside the stream the browser plays (the remux carries the preferred one); others are streamed separately.</summary>
    public int? AudioDefaultIndex { get; set; }
    /// <summary>Audio track the player starts with: first preferred language, else the file's default.</summary>
    public int? AudioStartIndex { get; set; }
    /// <summary>Subtitle track the player turns on at the start: one in a preferred language, else (video without subtitles of its own) a subtitle file found for it.</summary>
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
    public SubtitleTrackDto? GetDefaultSubtitleTrack() => !SubtitleDefaultIndex.HasValue ? null : SubtitleTracks.FirstOrDefault(t => t.Index == SubtitleDefaultIndex.Value);
}

public class AudioTrackDto
{
    public required int Index { get; set; }
    public required string Label { get; set; }
    public string? Language { get; set; }
    /// <summary>English name of <see cref="Language"/> ("eng" → "English").</summary>
    public string? LanguageName { get; set; }
    public required string Codec { get; set; }
}

public class SubtitleTrackDto
{
    /// <summary>First index of subtitle files found for the video (<see cref="ExternalPath"/>); below it = stream index in the file.</summary>
    public const int ExternalIndexBase = 10_000;

    public required int Index { get; set; }
    public required string Label { get; set; }
    public string? Language { get; set; }
    /// <summary>English name of <see cref="Language"/> ("eng" → "English").</summary>
    public string? LanguageName { get; set; }
    public required string Format { get; set; }
    /// <summary>Subtitle file outside the video (<see cref="Popcorn.Application.Utils.ExternalSubtitles"/>), null for the video's own tracks.</summary>
    [JsonIgnore]
    public string? ExternalPath { get; set; }
}

public enum SubtitleFormat
{
    Unknown = 0,
    Srt,
    Ass,
    Vtt,
    Pgs
}