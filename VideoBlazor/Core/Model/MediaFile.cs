using VideoBlazor.Core.Enums;

namespace VideoBlazor.Core.Model
{
    public class MediaFile
    {
        public string Path { get; set; }
        public string Title { get; set; }
        public FileType Type { get; set; }
    }
}
