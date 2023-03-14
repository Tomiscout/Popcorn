using VideoBlazor.Shared.Enum;
using VideoBlazor.Shared.Models;

namespace VideoBlazor.SearchProviders.Models
{
    public class SearchQueryResult
    {
        public SearchSourceType Source { get; set; }
        public List<SearchResult> Results { get; set; }
        public string ErrorMessage { get; set; }
        public Exception Error { get; set; }
        public bool Success { get; set; }
    }
}
