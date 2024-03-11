using System.Text.Json.Serialization;
using Popcorn.Domain.Enum;
using Popcorn.Shared.Utils;

namespace Popcorn.Domain.Models;

public class MediaFile
{
    public Guid? Id { get; set; } = Guid.NewGuid();
    public Guid ItemId { get; set; }
    public string Path { get; set; }  //TODO this should be renamed to FilePath, but file migration would be needed. Or just keep it whatever
    public string Title { get; set; }
    public string ShortTitle { get; set; } = string.Empty;
    public FileType Type { get; set; } = FileType.File;
    public bool Played { get; set; } = false;
    public bool IntroProcessed { get; set; } = false;
    public bool OutroProcessed { get; set; } = false;

    public List<SegmentLocation> SegmentLocations { get; set; } = new ();

    public List<string> EpisodeTags { get; set; }
    public List<string> FormatTags { get; set; }
    public List<string> OtherTags { get; set; }

    public string GetDisplayTitle()
    {
        if(Type == FileType.Folder)
        {
            return Title;
        }

        if(string.IsNullOrWhiteSpace(ShortTitle))
        {
            ParseTitle();
        }
        return string.IsNullOrWhiteSpace(ShortTitle) ? Title : ShortTitle;
    }

    public void ParseTitle()
    {
        if (string.IsNullOrWhiteSpace(Title))
        {
            Title = System.IO.Path.GetFileName(Path); //Could be implemented in getter setter
        }
        if(FormatTags is null)
        {
            ShortTitle = Title;
            return;
        }
        var (parsed, tags) = StringUtils.SanitizeFilename(Title, FormatTags);
        ShortTitle = parsed;
        FormatTags = tags;
    }

    public string GetIntroFilename() => $"{Id}-intro";
    public string GetOutroFilename() => $"{Id}-outro";
}