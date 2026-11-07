using Popcorn.FilenameParser;

namespace Popcorn.Application.Extensions;

/// <summary>Library item names from folder / release names.</summary>
public static class MediaTitle
{
    /// <summary>
    /// The name of a new library item: release tags dropped ("Show.S01.1080p.WEB-GRP" -> "Show",
    /// "Movie.2010.1080p.BluRay.x264-GRP" -> "Movie (2010)"); a name without tags stays as given.
    /// </summary>
    public static string FromName(string name)
    {
        var trimmed = name.Trim();
        // Folder and release names have no extension; a dot-separated tag ("x264-GRP") would be taken for one.
        var parsed = MediaNameParser.Parse(trimmed + ".mkv");
        if (!HasReleaseTags(parsed) || string.IsNullOrWhiteSpace(parsed.Title)) return trimmed;
        // A show's year is part of its parsed title already ("Doctor Who 2005").
        return !parsed.IsTv && parsed.Year is { } year ? $"{parsed.Title} ({year})" : parsed.Title;
    }

    /// <summary>The name has tags only a release name has (a plain name such as "1917" or "Show (2005)" has none).</summary>
    internal static bool HasReleaseTags(ParsedMediaName parsed) =>
        parsed.Resolution.HasValue || parsed.VideoCodec is not null || parsed.Source is not null || parsed.Seasons.Count > 0;
}
