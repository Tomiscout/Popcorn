using VideoBlazor.Shared.Enum;
using VideoBlazor.SearchProviders.Models;

namespace VideoBlazor.SearchProviders
{
	internal interface ISearchProvider
	{
        public SearchSourceType Source { get; }
        public bool Imdb { get; }
        public Task<SearchQueryResult> SearchImdb(string imdb, string title);
        public Task<SearchQueryResult> Search(string query);
    }
}
