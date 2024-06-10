using Popcorn.Domain.Models;
using Popcorn.Domain.Models.Torrent;
using Popcorn.Shared.Models;

namespace Popcorn.Domain.Interfaces;

public interface ITorrentCore
    {
        List<TorrentViewModel> GetTorrentsDisplay();
        
        Task StartTorrents(List<Guid> ids);
        
        Task StopTorrents(List<Guid> ids);
        
        Task StartMetadataFetch(Guid id);
        
        Task SubmitTorrentAdd(TorrentAddModel model);
        
        Task AddTorrentFile(string path);
        
        Task AddTorrentMagnet(string magnet);
        
        Task CancelTorrentAdd(Guid id);
        
        Task RemoveTorrent(Guid id, bool deleteFiles);
        
        Task RemoveTorrents(List<Guid> ids, bool deleteFiles);
        
        Task StartTorrentDownload(SearchResult item, bool builtIn);
    }