using Popcorn.Shared.Ffmpeg;

namespace Popcorn.Server.Controllers.Models;

public class MediaFileMetadata
{
    public Guid FileId { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string FullTitle { get; set; } = string.Empty;
    public string ShortTitle { get; set; } = string.Empty;
    public bool IsTvShow { get; set; } = false;
    public int Season { get; set; }
    public int Episode { get; set; }
    public List<AudioTrackDto> AudioTracks { get; set; } = [];
    public List<SubtitleTrackDto> SubtitleTracks { get; set; } = [];
    public VideoCodec? VideoDefaultCodec { get; set; }
    public int? AudioDefaultIndex { get; set; }
    public AudioCodec? AudioDefaultCodec { get; set; }
    public int? SubtitleDefaultIndex { get; set; }
    public List<string> FormatNames { get; set; } = [];
}

public class AudioTrackDto
{
    public required string Key { get; set; }
    public required string Label { get; set; }
    public string? Language { get; set; }
    public string Codec { get; set; }
}

public class SubtitleTrackDto
{
    public required string Key { get; set; }
    public required string Label { get; set; }
    public string? Language { get; set; }
    public string Format { get; set; }
}

public enum SubtitleFormat
{
    Unknown = 0,
    Srt,
    Ass,
    Vtt,
    Pgs
}