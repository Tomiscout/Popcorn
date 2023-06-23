using VideoBlazor.Shared.Enum;

namespace VideoBlazor.Shared.Models
{
    public class SearchResult
    {
        public string Title { get; set; }
        public int Seeds { get; set; }
        public int Peers { get; set; }
        public string MagnetLink { get; set; }
        public string TorrentFileLink { get; set; }
        public string TorrentFilePath { get; set; }
        public SearchSourceType Source { get; set; }
        public List<string> Genres { get; set; }
        public List<string> Tags { get; set; }
        public string Image { get; set; }
        public string Description { get; set; }
        public DateTime UploadedDate { get; set; }
        public long Size { get; set; }
        public int FilesCount { get; set; }
        public string Imdb { get; set; }
        public string Category { get; set; }
        public string TorrentPageUrl { get; set; }

        public List<string> EpisodeTags { get; set; }
        public List<string> FormatTags { get; set; }
        public List<string> OtherTags { get; set; }
    }
}
