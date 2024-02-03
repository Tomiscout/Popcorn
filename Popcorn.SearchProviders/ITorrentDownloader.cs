
namespace Popcorn.SearchProviders
{
    internal interface ITorrentDownloader
    {
        public Task<string> DownloadTorrent(string url);
    }
}
