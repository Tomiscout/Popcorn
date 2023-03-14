using VideoBlazor.SearchProviders.Models;
using VideoBlazor.Shared.Enum;

namespace VideoBlazor.SearchProviders.Providers.Yts
{
    internal class YtsProvider : ISearchProvider
    {
        //https://yts.torrentbay.to/api
        //https://www.nuget.org/packages/YTS.NET
        public SearchSourceType Source => throw new NotImplementedException();

        public bool Imdb => throw new NotImplementedException();

        public Task<SearchQueryResult> Search(string query)
        {
            throw new NotImplementedException();
        }

        public Task<SearchQueryResult> SearchImdb(string imdb, string title)
        {
            throw new NotImplementedException();
        }
    }
}
