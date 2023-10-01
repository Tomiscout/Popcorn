using System.Diagnostics;
using System.Net;
using VideoBlazor.Shared;
using VideoBlazor.Shared.Models;
using VideoBlazor.Shared.Utils;
using VideoBlazor.Torrents;

namespace VideoBlazor.SearchProviders.Utils
{
    public class TorrentUtils
    {
        private static readonly string _imageFolder = Path.Join(DirUtils.DataFolder, "torrents\\");
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
