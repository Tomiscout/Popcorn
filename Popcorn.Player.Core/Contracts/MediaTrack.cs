using System.Text.Json.Serialization;
using Popcorn.Shared.Languages;

namespace Popcorn.Player.Core.Contracts;

public class MediaTrack
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("type")]
    public MediaTrackType Type { get; set; } = MediaTrackType.Unknown;

    [JsonPropertyName("language")]
    public string Language { get; set; }

    /// <summary>English name of <see cref="Language"/> ("eng" → "English"), empty without a language.</summary>
    [JsonPropertyName("languageName")]
    public string LanguageName => LanguageCodes.DisplayName(Language) ?? string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; }

    [JsonPropertyName("selected")]
    public bool Selected { get; set; }

    /// <summary>mpv's codec name ("subrip", "ass", "hdmv_pgs_subtitle"...): the overlay's subtitle syncer skips bitmap subtitles.</summary>
    [JsonPropertyName("codec")]
    public string Codec { get; set; } = string.Empty;

    /// <summary>Stream index in the file (mpv <c>ff-index</c>), null for tracks from other files.</summary>
    [JsonIgnore]
    public int? StreamIndex { get; set; }

    /// <summary>File the track comes from when it is not the video's own (subtitle files).</summary>
    [JsonIgnore]
    public string? ExternalFilename { get; set; }

    public static MediaTrackType ParseTrackType(string type) => type switch
    {
        "video" => MediaTrackType.Video,
        "audio" => MediaTrackType.Audio,
        "sub" => MediaTrackType.Subtitle,
        _ => MediaTrackType.Unknown,
    };
}

public enum MediaTrackType
{
    Video,
    Audio,
    Subtitle,
    Unknown,
}
