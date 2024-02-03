using System.Text.Json.Serialization;

namespace Popcorn.SearchProviders.Providers.Rarbg.DTO
{
    public class RarbgEpisodeInfo
    {
        [JsonPropertyName("imdb")]
        public string Imdb { get; set; }

        [JsonPropertyName("tvrage")]
        public string TvRage { get; set; }

        [JsonPropertyName("tvdb")]
        public string TvDb { get; set; }

        [JsonPropertyName("themoviedb")]
        public string TheMovieDb { get; set; }
    }
}
