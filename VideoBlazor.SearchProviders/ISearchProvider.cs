using VideoBlazor.SearchProviders.Enum;
using VideoBlazor.SearchProviders.Models;

namespace VideoBlazor.SearchProviders
{
	internal interface ISearchProvider
	{
        public SearchSourceType Source { get; }
        public Task<SearchQueryResult> Search(string query);
    }
}
