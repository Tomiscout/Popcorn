using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using VideoBlazor.SearchProviders.Enum;
using VideoBlazor.SearchProviders.Model;
using VideoBlazor.SearchProviders.Models;
using VideoBlazor.SearchProviders.Utils;

namespace VideoBlazor.SearchProviders.Providers.PirateBay
{
    public class PirateBaySearchProvider : ISearchProvider
    {
        private readonly string[] _pirateBayTrackers = {
            "udp://tracker.coppersurfer.tk:6969/announce",
            "udp://tracker.openbittorrent.com:6969/announce",
            "udp://9.rarbg.to:2710/announce",
            "udp://9.rarbg.me:2780/announce",
            "udp://9.rarbg.to:2730/announce",
            "udp://tracker.opentrackr.org:1337",
            "http://p4p.arenabg.com:1337/announce",
            "udp://tracker.torrent.eu.org:451/announce",
            "udp://tracker.tiny-vps.com:6969/announce",
            "udp://open.stealth.si:80/announce",
        };

        public async Task<SearchQueryResult> Search(string query)
        {
            var searchQuery = WebUtility.UrlEncode(query);
            var url = $"https://apibay.org/q.php?q={searchQuery}";
            using var client = new HttpClient();
            var content = await client.GetStringAsync(url);
            var results = JsonSerializer.Deserialize<List<PirateBaySearchResult>>(content);
            return new SearchQueryResult
            {
                Results = results.Select(x => new SearchResult
                {
                    Title = x.Name,
                    Seeds = int.Parse(x.Seeders),
                    Peers = int.Parse(x.Leechers),
                    MagnetLink = TorrentUtils.BuildMagnetLink(x.Name, x.InfoHash, _pirateBayTrackers),
                    Source = SearchSourceType.PirateBay,
                    Size = long.Parse(x.Size)
                }).ToList(),
                Source = SearchSourceType.PirateBay,
                Success = true
            };
        }
    }
}
