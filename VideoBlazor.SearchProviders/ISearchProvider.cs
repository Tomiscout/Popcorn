using VideoBlazor.SearchProviders.Models;

namespace VideoBlazor.SearchProviders
{
	internal interface ISearchProvider
	{

        public Task<SearchQueryResult> Search(string query);
    }
}
