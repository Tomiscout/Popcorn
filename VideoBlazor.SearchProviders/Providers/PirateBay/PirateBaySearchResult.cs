using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace VideoBlazor.SearchProviders.Providers.PirateBay
{
	public class PirateBaySearchResult
	{
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("info_hash")]
        public string InfoHash { get; set; }

        [JsonPropertyName("seeders")]
        public string Seeders { get; set; }

        [JsonPropertyName("leechers")]
        public string Leechers { get; set; }

        [JsonPropertyName("num_files")]
        public string FilesCount { get; set; }

        [JsonPropertyName("size")]
        public string Size { get; set; }

        [JsonPropertyName("category")]
        public string Category { get; set; }

        [JsonPropertyName("username")]
        public string Uploader { get; set; }

        [JsonPropertyName("imdb")]
        public string Imdb { get; set; }

        [JsonPropertyName("status")]
        public string Status { get; set; }

        [JsonPropertyName("added")]
        public string UploadedTime { get; set; }
    }
}
