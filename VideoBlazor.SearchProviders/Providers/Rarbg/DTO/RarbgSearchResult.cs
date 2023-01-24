using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace VideoBlazor.SearchProviders.Providers.Rarbg.DTO
{
    public class RarbgSearchResult
    {
        [JsonPropertyName("title")]
        public string Title { get; set; }

        [JsonPropertyName("category")]
        public string Category { get; set; }

        [JsonPropertyName("download")]
        public string MagnetLink { get; set; }

        [JsonPropertyName("seeders")]
        public int Seeds { get; set; }

        [JsonPropertyName("leechers")]
        public int Peers { get; set; }

        [JsonPropertyName("size")]
        public long Size { get; set; }

        [JsonPropertyName("pubdate")]
        public string UploadDate { get; set; }

        [JsonPropertyName("episode_info")]
        public RarbgEpisodeInfo EpisodeInfo { get; set; }

        [JsonPropertyName("ranked")]
        public int Ranked { get; set; }

        [JsonPropertyName("info_page")]
        public string TorrentPageUrl { get; set; }
    }
}
