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

        [JsonPropertyName("episodeTitle")]
        public string EpisodeTitle { get; set; }
        
        [JsonPropertyName("season")]
        public int? Season { get; set; }
        
        [JsonPropertyName("episode")]
        public int? Episode { get; set; }
    }
}
