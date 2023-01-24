using System.Net;

namespace VideoBlazor.SearchProviders.Utils
{
    public static class TorrentUtils
    {

        public static string BuildMagnetLink(string name, string infoHash, string[] trackers)
        {
            var magnet = $"magnet:?xt=urn:btih:{infoHash}&dn={name}";
            foreach (var tracker in trackers)
            {
                magnet += $"&tr={WebUtility.UrlEncode(tracker)}";
            }
            return magnet;
        }
    }
}
