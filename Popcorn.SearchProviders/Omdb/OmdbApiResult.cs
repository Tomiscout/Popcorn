using System.Text.Json.Serialization;

namespace Popcorn.SearchProviders.Omdb
{
    public class OmdbApiResult
    {
        [JsonPropertyName("Metascore")]
        public string Metascore { get; set; }

        [JsonPropertyName("imdbRating")]
        public string ImdbRating { get; set; }

        [JsonPropertyName("Ratings")]
        public List<ObdbResultRatings> Ratings { get; set; }

        [JsonPropertyName("Awards")]
        public string Awards { get; set; }
    }

    public class ObdbResultRatings {
        [JsonPropertyName("Source")]
        public string Source { get; set; }

        [JsonPropertyName("Value")]
        public string Value { get; set; }
    }
}
