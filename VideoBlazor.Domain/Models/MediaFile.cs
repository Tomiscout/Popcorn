using VideoBlazor.Domain.Enum;
using VideoBlazor.Shared.Utils;

namespace VideoBlazor.Domain.Models;

public class MediaFile
{
    public string Path { get; set; }
    public string Title { get; set; }
    public string ShortTitle { get; set; } = null;
    public FileType Type { get; set; }

    public List<string> EpisodeTags { get; set; }
    public List<string> FormatTags { get; set; }
    public List<string> OtherTags { get; set; }

    public string GetDisplayTitle()
    {
        return Type == FileType.Folder || string.IsNullOrWhiteSpace(ShortTitle) ? Title : ShortTitle; //Controversial
    }

    public void ParseTitle()
    {
        var (parsed, tags) = StringUtils.SanitizeFilename(Title, FormatTags.ToArray());
        ShortTitle = parsed;
        FormatTags = tags;
    }
}
