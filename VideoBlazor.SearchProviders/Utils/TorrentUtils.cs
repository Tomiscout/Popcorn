using System.Diagnostics;
using System.Net;
using VideoBlazor.Shared;
using VideoBlazor.Shared.Models;
using VideoBlazor.Torrents;

namespace VideoBlazor.SearchProviders.Utils
{
    public static class TorrentUtils
    {
        private static readonly bool _builtInTorrentClient = true; //TODO get this from settings
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

        public static void OpenMagnetLinkSystem(string magnetLink)
        {
            var command = FileAssociation.GetExecFileAssociatedToExtension(".torrent");
            Process.Start(command, $"\"{magnetLink}\"");
        }
        
        public static void OpenTorrentFileSystem(string path)
        {
            Process.Start(path);
        }

        public static async Task StartTorrentDownload(SearchResult item)
        {
            if(!string.IsNullOrWhiteSpace(item.MagnetLink)) {
                if (_builtInTorrentClient)
                {
                    TorrentCore.AddTorrentMagnet(item.MagnetLink);
                }
                else
                {
                    OpenMagnetLinkSystem(item.MagnetLink);
                }
            }
            else
            {
                var path = await SearchProviders.DownloadTorrent(item.Source, item.TorrentFileLink);
                //TODO Catch and log
                if (_builtInTorrentClient)
                {
                    await TorrentCore.AddTorrentFile(path);
                }
                else
                {
                    OpenTorrentFileSystem(item.MagnetLink);
                }
            }
        }
    }
}
