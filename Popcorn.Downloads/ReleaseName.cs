using System.Collections.Concurrent;
using Popcorn.FilenameParser;

namespace Popcorn.Downloads;

/// <summary>Readable names of releases ("Show.Name.S01E02.1080p.WEB.x264-GRP" -> "Show Name S01E02 · 1080p").</summary>
public static class ReleaseName
{
    // The list is polled every second; the names rarely change.
    private static readonly ConcurrentDictionary<string, string> Cache = new();

    /// <summary>Title (year), season/episodes and resolution from <see cref="MediaNameParser"/>; the name itself when no title is found.</summary>
    public static string Display(string name) => Cache.GetOrAdd(name, Format);

    private static string Format(string name)
    {
        var parsed = MediaNameParser.Parse(name);
        if (string.IsNullOrWhiteSpace(parsed.Title)) return name;

        var parts = new List<string> { parsed.Year is { } year ? $"{parsed.Title} ({year})" : parsed.Title };
        var episodes = Episodes(parsed);
        if (episodes.Length > 0) parts.Add(episodes);
        var display = string.Join(' ', parts);
        return parsed.Resolution is { } resolution ? $"{display} · {resolution}p" : display;
    }

    private static string Episodes(ParsedMediaName parsed)
    {
        var season = parsed.Seasons.Count switch
        {
            0 => "",
            1 => $"S{parsed.Seasons[0]:00}",
            _ => $"S{parsed.Seasons.Min():00}–S{parsed.Seasons.Max():00}",
        };
        if (parsed.Episodes.Count == 0) return season;
        var first = parsed.Episodes.Min();
        var last = parsed.Episodes.Max();
        return first == last ? $"{season}E{first:00}" : $"{season}E{first:00}–E{last:00}";
    }
}
