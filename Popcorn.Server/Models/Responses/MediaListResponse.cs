using Popcorn.Application.Extensions;
using Popcorn.Domain.Models;
using Popcorn.FilenameParser;

namespace Popcorn.Server.Models.Responses;

public class MediaListResponse
{
    public List<MediaItem>? RecentItems { get; set; }
    public List<MediaItem>? NewItems { get; set; }
    public List<MediaItem>? RemovedItems { get; set; }
    /// <summary>Per item id: the file its card plays (shown on hover).</summary>
    public Dictionary<Guid, ContinueWatching> Continue { get; set; } = [];
}

/// <summary>The file "continue watching" plays (<c>MediaItemExtensions.GetContinueFile</c>).</summary>
public class ContinueWatching
{
    public Guid FileId { get; set; }
    /// <summary>"S01 E08" / "E05", null when the file has no episode number.</summary>
    public string? Label { get; set; }
    /// <summary>Episode name from the file name, null if it has none.</summary>
    public string? EpisodeTitle { get; set; }
    /// <summary>Seconds; null when the file was not started (or its position is unknown).</summary>
    public double? Position { get; set; }
    public double? Duration { get; set; }

    /// <summary>Needs the item's files and history; null when it has no file to play.</summary>
    public static ContinueWatching? For(MediaItem item)
    {
        var file = item.GetContinueFile();
        if (file is null) return null;

        if (!file.TagsProcessed) file.ParseTags();
        return new ContinueWatching
        {
            FileId = file.Id,
            Label = EpisodeLabel(file),
            EpisodeTitle = MediaNameParser.Parse(file.Path).EpisodeTitle,
            Position = file.GetResumePosition(),
            Duration = file.Duration,
        };
    }

    private static string? EpisodeLabel(MediaFile file) => (file.SeasonNumber, file.EpisodeNumber) switch
    {
        ({ } season, { } episode) => $"S{season:00} E{episode:00}",
        (null, { } episode) => $"E{episode:00}",
        _ => null,
    };
}
