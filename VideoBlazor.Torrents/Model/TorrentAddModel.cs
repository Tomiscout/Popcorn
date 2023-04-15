namespace VideoBlazor.Torrent.Model;

public class TorrentAddModel
{
    public Guid Id { get; set; }
    public string TorrentPath { get; set; }
    public string Magnet { get; set; }
    public string DownloadDirectory { get; set; }
    public bool SequentialDownload { get; set; } = true;
    
    public string Title { get; set; }
}