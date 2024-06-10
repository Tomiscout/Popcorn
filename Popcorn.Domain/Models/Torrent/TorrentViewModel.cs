namespace Popcorn.Domain.Models.Torrent;

public class TorrentViewModel
{
    public Guid Id { get; set; }
    
    public string Title { get; set; }
    
    public string Status { get; set; }
    public double Progress { get; set; }
    
    public string Size { get; set; }
    
    public int Seeds { get; set; }
    
    public int Peers { get; set; }
    
    public string DownloadSpeed { get; set; }
    
    public string UploadSpeed { get; set; }
    
    public string ETA { get; set; }

    public double Ratio { get; set; }
}