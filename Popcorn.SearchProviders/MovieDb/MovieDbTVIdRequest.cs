using System.Text.Json.Serialization;

namespace Popcorn.SearchProviders.MovieDb
{
    public class MovieDbTVIdRequest
    {
        [JsonPropertyName("imdb_id")]
        public string ImdbId { get; set; }
    }
}
