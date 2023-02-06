using System.Text.Json.Serialization;

namespace VideoBlazor.SearchProviders.MovieDb
{
    public class MovieDbTVIdRequest
    {
        [JsonPropertyName("imdb_id")]
        public string ImdbId { get; set; }
    }
}
