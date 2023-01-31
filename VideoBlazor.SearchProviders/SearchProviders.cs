using VideoBlazor.SearchProviders.Enum;
using VideoBlazor.SearchProviders.Model;
using VideoBlazor.SearchProviders.Models;
using VideoBlazor.SearchProviders.Providers.Linkomanija;
using VideoBlazor.SearchProviders.Providers.PirateBay;
using VideoBlazor.SearchProviders.Providers.Rarbg;

namespace VideoBlazor.SearchProviders
{
    
    // All the code in this file is included in all platforms.
    public static class SearchProviders
    {

        private static List<ISearchProvider> _searchProviders = new List<ISearchProvider>()
        {
            /*new PirateBaySearchProvider(),
            new RarbgSearchProvider(),*/
            new LinkomanijaProvider(),
        };
        
        public static List<SearchSourceType> GetSearchProvidersSources()
        {
            return _searchProviders.Select(s => s.Source).ToList();
        }

        //TODO parallel runner https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/concepts/async/start-multiple-async-tasks-and-process-them-as-they-complete?pivots=dotnet-7-0
        public static async Task SearchQuery(string query, Delegate resultHandler)
        {
            IEnumerable<Task> searchTasksQuery =
            from provider in _searchProviders
            select ProcessSearchProviderAsync(provider, query, resultHandler);

            List<Task> downloadTasks = searchTasksQuery.ToList();

            while (downloadTasks.Any())
            {
                Task finishedTask = await Task.WhenAny(downloadTasks);
                downloadTasks.Remove(finishedTask);
            }
        }

        static async Task ProcessSearchProviderAsync(ISearchProvider provider, string query, Delegate resultHandler)
        {
            try
            {
                var results = await provider.Search(query);
                resultHandler.DynamicInvoke(results);
            } catch(Exception e)
            {
                //TODO exception handling
            }

        }

        public static async Task DownloadAndOpenTorrent(SearchSourceType source, string torrentUrl)
        {
            var provider = _searchProviders.FirstOrDefault(s => s.Source == source);
            if(provider is null) return;
            ((ITorrentDownloader)provider).DownloadTorrent(torrentUrl);
        }
    }
}