using System.Diagnostics;
using MonoTorrent;
using MonoTorrent.Client;
using VideoBlazor.Shared;
using VideoBlazor.Shared.Models;
using VideoBlazor.Shared.Utils;
using VideoBlazor.Torrent.Model;

namespace VideoBlazor.Torrents;
    
public class TorrentCore
{
    private static readonly bool _builtInTorrentClient = true; //TODO get this from settings
    private string _defaultDownloadFolder = @"C:\Users\brazi\Downloads\test";
    private ClientEngine _engine;
    private Dictionary<Guid, TorrentManager> _managers = new Dictionary<Guid, TorrentManager>();

    private TorrentAddModel _torrentToAdd = null;


    public TorrentCore()
    {
        _engine = new ClientEngine(new EngineSettings()
        {

        });
        LoadSavedTorrents().ConfigureAwait(false);
    }
  
    public List<TorrentViewModel> GetTorrentsDisplay()
    {
        var returnList = new List<TorrentViewModel>();
        foreach (var t in _managers)
        {
            if (t.Value.Torrent == null) continue;
            
            returnList.Add(new TorrentViewModel
            {
                Id = t.Key,
                Title = t.Value.Torrent.Name,
                Progress = Math.Round(t.Value.Progress, 2),
                Size = StringUtils.FormatBytes(t.Value.Torrent.Size),
                Seeds = 0,
                Peers = t.Value.OpenConnections,
                DownloadSpeed = StringUtils.FormatBytes(t.Value.Monitor.DownloadSpeed, true),
                UploadSpeed = StringUtils.FormatBytes(t.Value.Monitor.UploadSpeed, true),
                Status = t.Value.State.ToString(),
                ETA = "TODO",
                Ratio = t.Value.Monitor.DataBytesDownloaded > 0 ? Math.Round((double)t.Value.Monitor.DataBytesUploaded / t.Value.Monitor.DataBytesDownloaded, 2) : 0
            });
        }

        return returnList;
    }

    public async Task StartTorrents(List<Guid> ids)
    {
        foreach (var id in ids)
        {
            await _managers[id].StartAsync();
        }
    }
    
    public async Task StopTorrents(List<Guid> ids)
    {
        foreach (var id in ids)
        {
            await _managers[id].StopAsync();
        }
    }
    
    public async Task<TorrentManager> StartMetadataFetch(Guid id)
    {
        await _managers[id].WaitForMetadataAsync();
        await _managers[id].StopAsync();
        return _managers[id];
    }

    public async Task SubmitTorrentAdd(TorrentAddModel model)
    {
        //Change settings, directory, file stuff
        //Start download
    }

    public async Task AddTorrentFile(string path)
    {
        await ResetTorrentToAdd();
        _torrentToAdd = new TorrentAddModel
        {
            Id = Guid.NewGuid(),
            DownloadDirectory = _defaultDownloadFolder,
            TorrentPath = path
        };
        await AddTorrentFromAddModel(_torrentToAdd);
    }

    public async Task AddTorrentMagnet(string magnet)
    {
        await ResetTorrentToAdd();
        _torrentToAdd = new TorrentAddModel
        {
            Id = Guid.NewGuid(),
            DownloadDirectory = _defaultDownloadFolder,
            Magnet = magnet,
        };
        await AddTorrentFromAddModel(_torrentToAdd);
    }

    private async Task AddTorrentFromAddModel(TorrentAddModel model)
    {
        TorrentManager manager;
        if (!string.IsNullOrWhiteSpace(model.Magnet))
        {
            manager = await _engine.AddAsync(MagnetLink.Parse(model.Magnet), model.DownloadDirectory, new TorrentSettings());
        }
        else
        {
            var torrent = await MonoTorrent.Torrent.LoadAsync(model.TorrentPath);
            manager = await _engine.AddAsync(torrent, model.DownloadDirectory, new TorrentSettings());
        }
        await InitManager(manager);
        _managers.Add(model.Id, manager);
        await manager.StartAsync();
    }

    public TorrentAddModel GetTorrentAddModel()
    {
        return _torrentToAdd;
    }
    
    private async Task LoadSavedTorrents()
    {
        //string[] torrentFiles = Directory.GetFiles(torrentDirectory, "*.torrent");

        var torrent = await MonoTorrent.Torrent.LoadAsync(@"C:\Users\brazi\Downloads\The.Last.of.Us.S01E08.When.We.Are.in.Need.1080p.AMZN.WEB-DL.DDP5.1.H.264-NTb.mkv.torrent");
        await LoadTorrent(torrent);
    }
    
    public async Task ResetTorrentToAdd()
    {
        if (_torrentToAdd is null) return;
        if(!_managers.ContainsKey(_torrentToAdd.Id))
        {
            _torrentToAdd = null;
            return;
        }

        var manager = _managers[_torrentToAdd.Id];
        await _engine.RemoveAsync(manager);
        _managers.Remove(_torrentToAdd.Id);
        
        _torrentToAdd = null;
    }


    public async Task RemoveTorrent(Guid id)
    {
        var manager = _managers[id];
        await manager.StopAsync();
        foreach (var file in manager.Files)
        {
            if (!File.Exists(file.FullPath)) continue;

            File.Delete(file.FullPath);
        }
        await _engine.RemoveAsync(manager);
        _managers.Remove(id);
    }

    public async Task RemoveTorrents(List<Guid> ids)
    {
        foreach (var id in ids)
        {
            await RemoveTorrent(id);
        }
    }

    private async Task LoadTorrent(MonoTorrent.Torrent torrent, string downloadDirectory = null)
    {
        //https://github.com/alanmcgovern/monotorrent/blob/master/src/Samples/SampleClient/StandardDownloader.cs
        var manager = await _engine.AddAsync(torrent, downloadDirectory ?? _defaultDownloadFolder, new TorrentSettings());
        await InitManager(manager);
        _managers.Add(Guid.NewGuid(), manager); //TODO load guid
    }
    
    public async Task StartTorrentDownload(SearchResult item, bool builtIn)
    {
        if(!string.IsNullOrWhiteSpace(item.MagnetLink)) {
            if (builtIn)
            {
                await AddTorrentMagnet(item.MagnetLink);
            }
            else
            {
                OpenMagnetLinkSystem(item.MagnetLink);
            }
        }
        else
        {
            //TODO Catch and log
            if (builtIn)
            {
                await AddTorrentFile(item.TorrentFilePath);
            }
            else
            {
                OpenTorrentFileSystem(item.MagnetLink);
            }
        }
    }
    
    private async Task InitManager(TorrentManager manager)
    {
        manager.TorrentStateChanged += delegate (object o, TorrentStateChangedEventArgs e) {
                
        };
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
}
