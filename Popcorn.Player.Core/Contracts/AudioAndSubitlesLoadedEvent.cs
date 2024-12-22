using System.Text.Json.Serialization;

namespace Popcorn.Player.Core.Contracts;

public class AudioAndSubtitlesLoadedEvent
{

    [JsonPropertyName("audioTracks")]
    public IList<MediaTrack> AudioTracks { get; set; }

    [JsonPropertyName("subtitleTracks")]
    public IList<MediaTrack> SubtitleTracks { get; set; }
}
