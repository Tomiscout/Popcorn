using MonoTorrent.Client;
using VideoBlazor.Torrent.Model;

namespace VideoBlazor.Torrents;
    
public static class TorrentCore
{
    private static ClientEngine _engine;
    private static Dictionary<Guid, TorrentManager> _managers = new Dictionary<Guid, TorrentManager>();
    public static void Initialize()
    {
        _engine = new ClientEngine(new EngineSettings()
        {

        });
        LoadSavedTorrents().ConfigureAwait(false);
    }
    private static async Task<List<TorrentManager>> LoadSavedTorrents()
    {
        var torrent = await MonoTorrent.Torrent.LoadAsync(@"C:\Users\brazi\Downloads\The.Last.of.Us.S01E08.When.We.Are.in.Need.1080p.AMZN.WEB-DL.DDP5.1.H.264-NTb.mkv.torrent");
        //string[] torrentFiles = Directory.GetFiles(torrentDirectory, "*.torrent");

        //https://github.com/alanmcgovern/monotorrent/blob/master/src/Samples/SampleClient/StandardDownloader.cs
        await _engine.AddAsync(torrent, @"C:\Users\brazi\Downloads\test", new TorrentSettings());
        foreach (var manager in _engine.Torrents)
        {
            await InitManager(manager);
            
            _managers.Add(Guid.NewGuid(), manager);
            
            //TODO dont start torrents with stop status
            //await manager.StartAsync();
            
        }
        
        return new List<TorrentManager>();
    }

    public static List<TorrentViewModel> GetTorrentsDisplay()
    {
        var returnList = new List<TorrentViewModel>();
        foreach (var t in _managers)
        {
            returnList.Add(new TorrentViewModel
            {
                Id = t.Key,
                Title = t.Value.Torrent.Name,
                Progress = Math.Round(t.Value.Progress, 2),
                Size = FormatBytes(t.Value.Torrent.Size),
                Seeds = 0,
                Peers = t.Value.OpenConnections,
                DownloadSpeed = FormatBytes(t.Value.Monitor.DownloadSpeed, true),
                UploadSpeed = FormatBytes(t.Value.Monitor.UploadSpeed, true),
                Status = t.Value.State.ToString(),
                ETA = "TODO",
                Ratio = t.Value.Monitor.DataBytesDownloaded > 0 ? Math.Round((double)t.Value.Monitor.DataBytesUploaded / t.Value.Monitor.DataBytesDownloaded, 2) : 0
            });
        }

        return returnList;
    }

    public static async Task StartTorrents(List<Guid> ids)
    {
        foreach (var id in ids)
        {
            await _managers[id].StartAsync();
        }
    }
    
    public static async Task StopTorrents(List<Guid> ids)
    {
        foreach (var id in ids)
        {
            await _managers[id].StopAsync();
        }
    }

    public static async Task AddTorrentFile(string path)
    {
        
    }
    
    public static async Task AddTorrentMagnet(string path)
    {
        
    }
    
    private static async Task InitManager(TorrentManager manager)
    {
        manager.TorrentStateChanged += delegate (object o, TorrentStateChangedEventArgs e) {
                
        };
    }
    
    private static string FormatBytes(long bytes, bool perSecond = false)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        int order = 0;
        while (bytes >= 1024 && order < sizes.Length - 1)
        {
            order++;
            bytes /= 1024;
        }

        var returnString = $"{bytes:0.#} {sizes[order]}";
        if (perSecond)
        {
            return $"{returnString}/s";
        }
        return returnString;
    }
}
