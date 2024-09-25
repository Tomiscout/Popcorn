using System.Text.Json.Serialization;

namespace Popcorn.Player.Linux.Contracts;

public class AudioAndSubtitlesLoadedEvent
{

    [JsonPropertyName("audioTracks")]
    public IList<VlcTrack> AudioTracks { get; set; }

    [JsonPropertyName("subtitleTracks")]
    public IList<VlcTrack> SubtitleTracks { get; set; }
}

public class VlcTrack
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("language")]
    public string Language { get; set; }

    [JsonPropertyName("description")]
    public string Description { get; set; }

    [JsonPropertyName("selected")]
    public bool Selected { get; set; }
}