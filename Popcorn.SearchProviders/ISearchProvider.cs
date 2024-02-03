using Popcorn.Shared.Enum;
using Popcorn.SearchProviders.Models;

namespace Popcorn.SearchProviders
{
	internal interface ISearchProvider
	{
        public SearchSourceType Source { get; }
        public bool Imdb { get; }
        public Task<SearchQueryResult> SearchImdb(string imdb, string title);
        public Task<SearchQueryResult> Search(string query);
    }
}
