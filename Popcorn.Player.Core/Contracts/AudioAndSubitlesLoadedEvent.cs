using System.Text.Json.Serialization;

namespace Popcorn.Player.Core.Contracts;

public class AudioAndSubtitlesLoadedEvent
{

    [JsonPropertyName("audioTracks")]
    public IList<MediaTrack> AudioTracks { get; set; }

    [JsonPropertyName("subtitleTracks")]
    public IList<MediaTrack> SubtitleTracks { get; set; }
}

public class MediaTrack
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("language")]
    public string Language { get; set; }

    [JsonPropertyName("description")]
    public string Description { get; set; }

    [JsonPropertyName("selected")]
    public bool Selected { get; set; }
}