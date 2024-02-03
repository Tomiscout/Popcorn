using Popcorn.SearchProviders.Providers.Linkomanija;
using Popcorn.SearchProviders.Providers.PirateBay;
using Popcorn.SearchProviders.Providers.Rarbg;
using Popcorn.Shared.Enum;

namespace Popcorn.SearchProviders
{
    
    // All the code in this file is included in all platforms.
    public static class SearchProviders
    {

        private static List<ISearchProvider> _searchProviders = new List<ISearchProvider>()
        {
            new PirateBaySearchProvider(),
            /*new RarbgSearchProvider(),
            new LinkomanijaProvider(),*/
        };
        
        public static List<SearchSourceType> GetSearchProvidersSources()
        {
            return _searchProviders.Select(s => s.Source).ToList();
        }

        public static List<SearchSourceType> GetImdbSearchProvidersSources()
        {
            return _searchProviders.Where(s => s.Imdb).Select(s => s.Source).ToList();
        }

        public static async Task SearchQuery(string query, Delegate resultHandler)
        {
            IEnumerable<Task> searchTasksQuery =
            from provider in _searchProviders
            select ProcessSearchProviderAsync(provider, query, resultHandler);

            List<Task> searchTasks = searchTasksQuery.ToList();

            while (searchTasks.Any())
            {
                Task finishedTask = await Task.WhenAny(searchTasks);
                searchTasks.Remove(finishedTask);
            }
        }

        public static async Task SearchImdbQuery(string query, string title, Delegate resultHandler)
        {
            IEnumerable<Task> searchTasksQuery =
            from provider in _searchProviders
            select ProcessSearchProviderImdbAsync(provider, query, title, resultHandler);

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

        static async Task ProcessSearchProviderImdbAsync(ISearchProvider provider, string query, string title, Delegate resultHandler)
        {
            try
            {
                var results = await provider.SearchImdb(query, title);
                resultHandler.DynamicInvoke(results);
            }
            catch (Exception e)
            {
                //TODO exception handling
            }
        }

        public static async Task<string> DownloadTorrent(SearchSourceType source, string torrentUrl)
        {
            var provider = _searchProviders.FirstOrDefault(s => s.Source == source);
            if(provider is null) return null;
            
            return await ((ITorrentDownloader)provider).DownloadTorrent(torrentUrl);
        }
    }
}