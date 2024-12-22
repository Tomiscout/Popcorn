using System.Text.Json.Serialization;

namespace Popcorn.Player.Core.Contracts;

public class MediaTrack
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("type")]
    public MediaTrackType Type { get; set; } = MediaTrackType.Unknown;

    [JsonPropertyName("language")]
    public string Language { get; set; }

    [JsonPropertyName("description")]
    public string Description { get; set; }

    [JsonPropertyName("selected")]
    public bool Selected { get; set; }

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
