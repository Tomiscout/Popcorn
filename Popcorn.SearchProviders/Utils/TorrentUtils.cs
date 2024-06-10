using System.Diagnostics;
using System.Net;
using Popcorn.Shared;
using Popcorn.Shared.Models;
using Popcorn.Shared.Utils;
using Popcorn.Torrents;

namespace Popcorn.SearchProviders.Utils
{
    public class TorrentUtils
    {
        private static readonly string _imageFolder = Path.Join(DirUtils.DataFolder, "torrents" + Path.DirectorySeparatorChar);
        public static string GetTorrentsFolder()
        {
            return _imageFolder;
        }

        private readonly TorrentCore _torrentCore;
        public TorrentUtils(TorrentCore torrentCore)
        {
            _torrentCore = torrentCore;
        }

        public static string BuildMagnetLink(string name, string infoHash, string[] trackers)
        {
            var magnet = $"magnet:?xt=urn:btih:{infoHash}&dn={name}";
            foreach (var tracker in trackers)
            {
                magnet += $"&tr={WebUtility.UrlEncode(tracker)}";
            }
            return magnet;
        }

        public static void EnsureTorrentsFolder()
        {
            Directory.CreateDirectory(GetTorrentsFolder());
        }
    }
}
