using System.Diagnostics;
using System.Reflection;
using MonoTorrent;
using MonoTorrent.Client;
using MonoTorrent.Client.PiecePicking;
using VideoBlazor.Domain.Models;
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
    private Dictionary<Guid, bool> _pendingManagers = new Dictionary<Guid, bool>(); //Bool represents if download dialog is skipped without waiting for metadata

	public static event EventHandler<TorrentAddModel> TorrentAddEvent;
	public static event EventHandler<Guid> MetadataFetchedEvent;

	public TorrentCore()
    {
        _engine = new ClientEngine(new EngineSettings()
        {

        });
        //LoadSavedTorrents().ConfigureAwait(false); TODO enable this later
    }
  
    public List<TorrentViewModel> GetTorrentsDisplay()
    {
        var returnList = new List<TorrentViewModel>();
        foreach (var t in _managers)
        {
            if (_pendingManagers.ContainsKey(t.Key) && !_pendingManagers[t.Key]) continue;
            
            returnList.Add(new TorrentViewModel
            {
                Id = t.Key,
                Title = t.Value.Torrent?.Name ?? "Fetching metadata...", //TODO get from model input somehow
                Progress = Math.Round(t.Value.Progress, 2),
                Size = t.Value.Torrent is not null ? StringUtils.FormatBytes(t.Value.Torrent.Size) : "?",
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
    
    public async Task StartMetadataFetch(Guid id)
    {
        await _managers[id].WaitForMetadataAsync();
        
        if(!_pendingManagers.ContainsKey(id)) return; //Canceled. RemoveTorrent is called from cancel event in the frontend

        if (!_pendingManagers[id])
        {
            await _managers[id].StopAsync(); //If download dialog is not skipped, stop torrent after metadata is fetched
        }
        
		MetadataFetchedEvent?.Invoke(this, id);
	}

    public async Task SubmitTorrentAdd(TorrentAddModel model)
    {
        var manager = GetManager(model.Id);

        _pendingManagers[model.Id] = true;
        if (!manager.HasMetadata)
        {
            //StartMetadataFetch haven't completed yet. To synchronize metadata is waited again.
            await manager.WaitForMetadataAsync();
        }

        await manager.StopAsync();
        if(model.DownloadDirectory != _defaultDownloadFolder)
        {
            await manager.MoveFilesAsync(model.DownloadDirectory, true);
        }
        if (model.SequentialDownload)
        {
            await manager.ChangePickerAsync(new StreamingPieceRequester());
        }
        await manager.StartAsync();
        
        if (!_pendingManagers.ContainsKey(model.Id)) return;
    }

    public TorrentManager GetManager(Guid id)
    {
        if (!_managers.ContainsKey(id)) return null;
        return _managers[id];
    }
    
    public async Task AddTorrentFile(string path)
    {
        var addModel = new TorrentAddModel //TODO probably this should be removed
        {
            Id = Guid.NewGuid(),
            DownloadDirectory = _defaultDownloadFolder,
            TorrentPath = path
        };
        await AddTorrentFromAddModel(addModel);
    }

    public async Task AddTorrentMagnet(string magnet)
    {
		var addModel = new TorrentAddModel
        {
            Id = Guid.NewGuid(),
            DownloadDirectory = _defaultDownloadFolder,
            Magnet = magnet,
        };
        await AddTorrentFromAddModel(addModel);
    }

    public async Task CancelTorrentAdd(Guid id)
    {
		_pendingManagers.Remove(id);
		await RemoveTorrent(id, true);
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

        _pendingManagers.Add(model.Id, false);
        OpenTorrentAddModal(model);

        //Not awaiting since it would freeze calling code, which shouldnt happen
        Task.Run(() => StartMetadataFetch(model.Id));
    }

    private void OpenTorrentAddModal(TorrentAddModel model)
    {
		Action<CancellationToken> modalAction = async tk1 => {
			await Task.Delay(TimeSpan.FromMilliseconds(200), tk1);
			TorrentAddEvent?.Invoke(this, model);
		};
		modalAction(default);
	}

    private async Task LoadSavedTorrents()
    {
        //string[] torrentFiles = Directory.GetFiles(torrentDirectory, "*.torrent");

        var torrent = await MonoTorrent.Torrent.LoadAsync(@"C:\Users\brazi\Downloads\The.Last.of.Us.S01E08.When.We.Are.in.Need.1080p.AMZN.WEB-DL.DDP5.1.H.264-NTb.mkv.torrent");
        await LoadTorrent(torrent);
    }

    public async Task RemoveTorrent(Guid id, bool deleteFiles)
    {
        var manager = GetManager(id);
        if (manager is null) return;
        
        await manager.StopAsync();

        if(deleteFiles && manager.Files is not null)
        {
            foreach (var file in manager.Files)
            {
                if (!File.Exists(file.FullPath)) continue;

                File.Delete(file.FullPath);
            }
        }
        
        await _engine.RemoveAsync(manager);
        _managers.Remove(id);
    }

    public async Task RemoveTorrents(List<Guid> ids, bool deleteFiles)
    {
        foreach (var id in ids)
        {
            await RemoveTorrent(id, deleteFiles);
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
