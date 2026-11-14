using System.Text.Json.Serialization;
using Popcorn.Domain.Enum;
using Popcorn.Shared.Utils;

namespace Popcorn.Domain.Models;

public class MediaFile
{
    public required Guid Id { get; set; } = Guid.NewGuid();
    public Guid ItemId { get; set; } = Guid.Empty;
    /// <summary>The <see cref="ItemSource"/> it came from (null: rows written by older versions, treated as the item folder's).</summary>
    public Guid? SourceId { get; set; }
    public string Path { get; set; }  //TODO this should be renamed to FilePath, but file migration would be needed. Or just keep it whatever
    public string Title { get; set; }
    public string ShortTitle { get; set; } = string.Empty;
    public FileType Type { get; set; } = FileType.File;
    public bool Played { get; set; } = false;
    public bool IntroProcessed { get; set; } = false;
    public bool OutroProcessed { get; set; } = false;

    /// <summary>
    /// Path in the item's file tree (not stored, set by <c>MediaTree.Build</c>): <see cref="Path"/> while the item has one
    /// source, else under a virtual folder per source inside the item's folder. Folder navigation and the folder walk of
    /// the next file solver use it; playing uses <see cref="Path"/>.
    /// </summary>
    public string? TreePath { get; set; }
    /// <summary>A source's folder in the tree, no row of its own (<see cref="Id"/> = the source's id).</summary>
    public bool IsVirtual { get; set; }

    public List<SegmentLocation> SegmentLocations { get; set; } = new ();

    public bool TagsProcessed { get; set; } = false;

    /// <summary>Last position the Popcorn players reported (seconds), null if never reported.</summary>
    public double? PlaybackPosition { get; set; }
    /// <summary>File duration (seconds) as reported with <see cref="PlaybackPosition"/>.</summary>
    public double? Duration { get; set; }
    
    public List<string>? Seasons { get; set; } = null;
    public List<string>? Episodes { get; set; } = null;
    public List<string>? FormatTags { get; set; }
    public List<string>? OtherTags { get; set; }

    /// <summary>First parsed season / episode number (parsed from the file path, see MediaFileTagExtensions.ParseTags), null if unknown.</summary>
    public int? SeasonNumber => FirstNumber(Seasons);
    public int? EpisodeNumber => FirstNumber(Episodes);

    private static int? FirstNumber(List<string>? values) =>
        values?.Select(v => int.TryParse(v, out var n) ? n : (int?)null).FirstOrDefault(n => n.HasValue);

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
        /*if(FormatTags is null)
        {
            ShortTitle = Title;
            return;
        }*/
        var tagsToRemove = new List<string>();
        if (FormatTags != null)
        {
            tagsToRemove.AddRange(FormatTags);
        }
        if (OtherTags != null)
        {
            tagsToRemove.AddRange(OtherTags);
        }
        var (parsed, _) = StringUtils.SanitizeFilename(Title, tagsToRemove);
        ShortTitle = parsed;
    }

    public string GetIntroFilename() => $"{Id}-intro";
    public string GetOutroFilename() => $"{Id}-outro";
}