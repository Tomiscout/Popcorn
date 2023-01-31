using System.Diagnostics;
using System.Net;
using VideoBlazor.Core.Utils;

namespace VideoBlazor.SearchProviders.Utils
{
    public static class TorrentUtils
    {
        private static readonly string _imageFolder = Path.Join(FileSystem.Current.AppDataDirectory, "torrents\\");
        public static string GetTorrentsFolder()
        {
            return _imageFolder;
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

        public static void OpenMagnetLink(string magnetLink)
        {
            var command = FileAssociation.GetExecFileAssociatedToExtension(".torrent");
            Process.Start(command, $"\"{magnetLink}\"");
        }
    }
}
