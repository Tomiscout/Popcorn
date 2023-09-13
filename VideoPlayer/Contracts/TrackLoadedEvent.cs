using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace VideoPlayer.Contracts
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
    }
}
