using VideoBlazor.Domain.Enum;
using VideoBlazor.Shared.Utils;

namespace VideoBlazor.Domain.Models;

public class MediaFile
{
    public string Path { get; set; }  //TODO this should be renamed to FilePath, but file migration would be needed. Or just keep it whatever
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
        if(FormatTags is null)
        {
            ShortTitle = Title;
            return;
        }
        var (parsed, tags) = StringUtils.SanitizeFilename(Title, FormatTags);
        ShortTitle = parsed;
        FormatTags = tags;
    }
}
