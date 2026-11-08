using Popcorn.Domain;
using Popcorn.FilenameParser;
using Serilog;

namespace Popcorn.Application.Utils;

/// <summary>A subtitle file found for a video (<see cref="ExternalSubtitles.Find"/>).</summary>
/// <param name="Title">Track name shown in the players: the file name without what it shares with the video.</param>
/// <param name="Language">ISO 639-2 code read from the name, null if none.</param>
public sealed record ExternalSubtitle(string Path, string Title, string? Language);

/// <summary>
/// Subtitle files of a video that are not inside it, searched in the item's folder (subfolders included, e.g. a
/// <c>Subs/</c> folder). A subtitle belongs to the video when
/// its name (minus a language/tag suffix: <c>Video.en.forced.srt</c>) or one of its folders
/// (<c>Subs/&lt;video name&gt;/English.srt</c>) is the video's name;
/// the folder holds only this one video (every subtitle in it is the video's);
/// or the subtitle's season/episode numbers are the video's and no other video there has them.
/// </summary>
public static class ExternalSubtitles
{
    /// <summary>Stop walking huge folders (an item whose folder is a whole library root).</summary>
    private const int MaxEntries = 20_000;

    /// <param name="videoPath">Local path of the video.</param>
    /// <param name="itemFolder">The library item's folder; the video's own folder when null or the video is not inside it.</param>
    public static List<ExternalSubtitle> Find(string videoPath, string? itemFolder = null)
    {
        try
        {
            return FindCore(videoPath, itemFolder);
        }
        catch (Exception e)
        {
            Log.Warning(e, "Could not look for subtitle files of {Path}", videoPath);
            return [];
        }
    }

    private static List<ExternalSubtitle> FindCore(string videoPath, string? itemFolder)
    {
        var videoDir = Path.GetDirectoryName(videoPath);
        if (string.IsNullOrEmpty(videoDir) || !Directory.Exists(videoDir)) return [];

        var root = itemFolder is not null && Directory.Exists(itemFolder) && IsInside(videoPath, itemFolder)
            ? Path.TrimEndingDirectorySeparator(itemFolder)
            : videoDir;

        var videos = new List<string>();
        var subtitles = new List<string>();
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, MatchCasing = MatchCasing.CaseInsensitive };
        var count = 0;
        foreach (var path in Directory.EnumerateFiles(root, "*", options))
        {
            if (++count > MaxEntries) break;
            var extension = Path.GetExtension(path).ToLowerInvariant();
            if (MediaLoader.SubtitleFormats.Contains(extension)) subtitles.Add(path);
            else if (MediaLoader.VideoFormats.Contains(extension)) videos.Add(path);
        }
        if (subtitles.Count == 0) return [];
        if (!videos.Contains(videoPath)) videos.Add(videoPath);

        // VobSub = .idx + .sub of the same name: the .idx is the track, the .sub only its data.
        var idx = subtitles.Where(s => s.EndsWith(".idx", StringComparison.OrdinalIgnoreCase))
            .Select(s => s[..^4]).ToHashSet(StringComparer.OrdinalIgnoreCase);
        subtitles.RemoveAll(s => s.EndsWith(".sub", StringComparison.OrdinalIgnoreCase) && idx.Contains(s[..^4]));

        var videoName = Path.GetFileNameWithoutExtension(videoPath);
        var episode = EpisodeKey(videoPath);
        // Parses every video found: only done when a subtitle needs it.
        var episodeIsUnique = new Lazy<bool>(() => episode is not null && videos.Count(v => EpisodeKey(v) == episode) == 1);

        var found = new List<ExternalSubtitle>();
        foreach (var subtitle in subtitles.Order(StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileNameWithoutExtension(subtitle);
            string title;
            if (SharesName(name, videoName))
            {
                title = name[videoName.Length..].Trim('.', '_', '-', ' ');
            }
            else if (videos.Count == 1 || HasFolderNamed(subtitle, root, videoName) ||
                     (episode is not null && EpisodeKey(subtitle) == episode && episodeIsUnique.Value))
            {
                title = name;
            }
            else continue;

            var language = LanguageParser.ParseSubtitleLanguage(subtitle);
            found.Add(new ExternalSubtitle(subtitle, title.Length > 0 ? title : Path.GetFileName(subtitle),
                language == Language.Unknown ? null : IsoLanguages.Get(language)?.ThreeLetterCode));
        }
        return found;
    }

    /// <summary>"Video", "Video.en", "Video_forced" for the video "Video".</summary>
    private static bool SharesName(string name, string videoName) =>
        name.StartsWith(videoName, StringComparison.OrdinalIgnoreCase) &&
        (name.Length == videoName.Length || name[videoName.Length] is '.' or '_' or '-' or ' ');

    /// <summary>A folder between <paramref name="root"/> and the file has the video's name (RARBG's Subs/&lt;video&gt;/2_English.srt).</summary>
    private static bool HasFolderNamed(string path, string root, string videoName)
    {
        for (var dir = Path.GetDirectoryName(path); dir is not null && dir.Length > root.Length; dir = Path.GetDirectoryName(dir))
        {
            if (Path.GetFileName(dir).Equals(videoName, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static string? EpisodeKey(string path)
    {
        var parsed = MediaNameParser.Parse(path);
        if (!parsed.IsTv || parsed.Episodes.Count == 0) return null;
        return $"{parsed.Seasons.FirstOrDefault()}:{string.Join(',', parsed.Episodes)}";
    }

    private static bool IsInside(string path, string folder)
    {
        var relative = Path.GetRelativePath(folder, path);
        return !relative.StartsWith("..") && !Path.IsPathRooted(relative);
    }
}
