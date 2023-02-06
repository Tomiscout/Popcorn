using System.Text.Json.Serialization;

namespace VideoBlazor.SearchProviders.MovieDb
{

    /// <summary>
    /// Standard response from an API call returning a more than one result, i.e., a Search Result.
    /// Single item based results are returned with an 
    /// <see cref="DM.MovieApi.ApiResponse.ApiQueryResponse{T}"/>.
    /// </summary>
    public class MovieDbApiResponse
    {
        /// <summary>
        /// Contains specific error information if an error was encountered during the API call to themoviedb.org.
        /// </summary>
        public MovieDbApiError Error { get; internal set; }

        /// <summary>
        /// The API command text used for the API call to themoviedb.org.
        /// </summary>
        public string CommandText { get; internal set; }

        /// <summary>
        /// The JSON returned from themoviedb.org based on the <see cref="CommandText"/> query.
        /// </summary>
        public string Json { get; internal set; }
        /// <summary>
        /// The list of results from the search.
        /// </summary>
        [JsonPropertyName("results")]
        public List<MovieDbMultiItem> Results { get; set; }

        /// <summary>
        /// The current page number of the search result.
        /// </summary>
        [JsonPropertyName("page")]
        public int PageNumber { get; set; }

        /// <summary>
        /// The total number of pages found from the search result.
        /// </summary>
        [JsonPropertyName("total_pages")]
        public int TotalPages { get; set; }

        /// <summary>
        /// The total number of results from the search.
        /// </summary>
        [JsonPropertyName("total_results")]
        public int TotalResults { get; set; }

        public override string ToString()
            => $"Page {PageNumber} of {TotalPages} ({TotalResults} total results)";
    }
}