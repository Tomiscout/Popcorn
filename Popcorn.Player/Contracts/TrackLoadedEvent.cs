using System.Text.Json.Serialization;

namespace Popcorn.Player.Contracts
{
    public class TrackLoadedEvent
    {
        [JsonPropertyName("filename")]
        public string FileName { get; set; }

        [JsonPropertyName("title")]
        public string Title { get; set; }

        [JsonPropertyName("shortTitle")]
        public string ShortTitle { get; set; }

        [JsonPropertyName("episodeTitle")]
        public string EpisodeTitle { get; set; }
        
        [JsonPropertyName("season")]
        public int? Season { get; set; }
        
        [JsonPropertyName("episode")]
        public int? Episode { get; set; }
    }
}
