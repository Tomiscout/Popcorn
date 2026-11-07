using Popcorn.Domain.Enum;
using Popcorn.Domain.Models;
using Popcorn.FilenameParser;

namespace Popcorn.Application.Extensions;

public static class MediaItemExtensions
{
    public static MediaFile GetNextMediaFile(this MediaItem item, IList<ItemHistory> history = null!)
    {
        if (item.Files is null) throw new InvalidOperationException("Attempting to get next item, but files are null");
        
        MediaFile result = null;

        history ??= item.History;
        var lastItem = history?.LastOrDefault();
        if (lastItem is null)
        {
            result = FindNextMediaFileInNewDirectory(item.FolderDirectory, item.Files, null);
            result ??= FindNextMediaFileInSubdirectories(item.FolderDirectory, item.Files, null);
            return result;
        }

        //If next file is sibling
        var lastItemFile = item.Files?.First(f => f.Id == lastItem.FileId);
        var historyPaths = history?.Select(h => item.Files!.First(f => f.Id == h.FileId).Path).ToList();

        var currentFolder = Directory.GetParent(lastItemFile.Path).FullName;
        result ??= FindNextMediaFileSibling(currentFolder, item.Files, lastItemFile.Path);
        result ??= FindNextMediaFileInSubdirectories(currentFolder, item.Files, historyPaths);
        if (result is not null)
        {
            return result;
        }

        //Moves to parent if not found yet.
        //When going to parent, shouldn't play already played directories
        while (currentFolder != item.FolderDirectory) //Test, may be dangerous
        {
            result = FindInParent(currentFolder, item.Files, historyPaths);
            if (result is not null)
            {
                return result;
            }
            currentFolder = Directory.GetParent(currentFolder).FullName;
        }

        return result;
    }

    /// <summary>
    /// What "continue watching" plays: the last played file when it was left before its end, otherwise the one after it
    /// (also when its position is unknown: external players, history from before positions were saved).
    /// Needs <see cref="MediaItem.Files"/> and <see cref="MediaItem.History"/>.
    /// </summary>
    public static MediaFile? GetContinueFile(this MediaItem item)
    {
        var files = item.Files ?? [];
        var last = (item.History ?? [])
            .Select(h => files.FirstOrDefault(f => f.Id == h.FileId))
            .LastOrDefault(f => f is not null);
        if (last is null) return item.GetNextMediaFile([]);
        if (!last.IsWatchedToEnd()) return last;

        var nextId = item.GetNextFilePlan(last.Id).NextFileId;
        return files.FirstOrDefault(f => f.Id == nextId) ?? last;
    }

    /// <summary>Left in the credits or later: the last 10 % of the file, at most 3 minutes. Unknown position counts as watched.</summary>
    public static bool IsWatchedToEnd(this MediaFile file) =>
        file.PlaybackPosition is not { } position || file.Duration is not { } duration || duration <= 0 ||
        position >= duration - Math.Min(180, duration * 0.1);

    /// <summary>Where opening the file starts: its saved position when it was left before the end, else null (beginning).</summary>
    public static double? GetResumePosition(this MediaFile file) => file.IsWatchedToEnd() ? null : file.PlaybackPosition;

    /// <summary>
    /// What plays after <paramref name="currentFileId"/>. With proper season/episode numbers that is the next higher
    /// episode (other versions of the same episode are passed over), and a hole in the numbering is reported in
    /// <see cref="NextFilePlan.MissingLabel"/>; otherwise the next file in folder order.
    /// </summary>
    public static NextFilePlan GetNextFilePlan(this MediaItem item, Guid currentFileId)
    {
        var plan = new NextFilePlan { CurrentFileId = currentFileId, ItemId = item.Id, ItemTitle = item.GetPlayerTitle() };

        var files = (item.Files ?? []).Where(f => f.Type == FileType.File).ToList();
        foreach (var file in files.Where(f => !f.TagsProcessed)) file.ParseTags();

        var current = files.FirstOrDefault(f => f.Id == currentFileId);
        if (current is null) return plan;

        var numbered = files.Where(f => f.SeasonNumber.HasValue && f.EpisodeNumber.HasValue).ToList();
        SetPreviousFile(plan, current, files, numbered);

        if (current.SeasonNumber is { } season && current.EpisodeNumber.HasValue && numbered.Count > 1)
        {
            var lastEpisode = current.Episodes!.Select(e => int.TryParse(e, out var n) ? n : 0).Max(); // S01E01E02
            var next = numbered
                .Where(f => f.SeasonNumber > season || (f.SeasonNumber == season && f.EpisodeNumber > lastEpisode))
                .OrderBy(f => f.SeasonNumber).ThenBy(f => f.EpisodeNumber).ThenBy(f => f.Path)
                .FirstOrDefault();
            if (next is null) return plan;

            plan.NextFileId = next.Id;
            plan.NextLabel = EpisodeLabel(next.SeasonNumber!.Value, next.EpisodeNumber!.Value);
            plan.NextEpisodeTitle = MediaNameParser.Parse(next.Path).EpisodeTitle;
            plan.MissingLabel = MissingBefore(season, lastEpisode, next.SeasonNumber.Value, next.EpisodeNumber.Value);
            return plan;
        }

        // The last played file decides the folder walk; history of deleted files would break it.
        var history = (item.History ?? [])
            .Where(h => files.Any(f => f.Id == h.FileId))
            .Append(new ItemHistory { ItemId = item.Id, FileId = currentFileId })
            .ToList();
        var nextFile = item.GetNextMediaFile(history);
        plan.NextFileId = nextFile?.Id;
        if (nextFile is not null)
        {
            plan.NextEpisodeTitle = MediaNameParser.Parse(nextFile.Path).EpisodeTitle;
            plan.NextTitle = WithoutPrefix(nextFile.GetDisplayTitle(), plan.ItemTitle);
        }
        return plan;
    }

    /// <summary>The next lower episode, or without proper numbers the file before it in folder order.</summary>
    private static void SetPreviousFile(NextFilePlan plan, MediaFile current, List<MediaFile> files, List<MediaFile> numbered)
    {
        MediaFile? previous;
        if (current.SeasonNumber is { } season && current.EpisodeNumber is { } episode && numbered.Count > 1)
        {
            previous = numbered
                .Where(f => f.SeasonNumber < season || (f.SeasonNumber == season && f.EpisodeNumber < episode))
                .OrderByDescending(f => f.SeasonNumber).ThenByDescending(f => f.EpisodeNumber).ThenBy(f => f.Path)
                .FirstOrDefault();
            if (previous is not null) plan.PreviousLabel = EpisodeLabel(previous.SeasonNumber!.Value, previous.EpisodeNumber!.Value);
        }
        else
        {
            // Folder first, then its subfolders (\0 sorts "a/b" before "a b"), as the walk of GetNextMediaFile goes.
            var ordered = files
                .OrderBy(f => (Path.GetDirectoryName(f.Path) ?? "").Replace(Path.DirectorySeparatorChar, '\0'), StringComparer.Ordinal)
                .ThenBy(f => f.Title).ThenBy(f => f.Path)
                .ToList();
            previous = ordered.ElementAtOrDefault(ordered.IndexOf(current) - 1);
            if (previous is not null) plan.PreviousTitle = WithoutPrefix(previous.GetDisplayTitle(), plan.ItemTitle);
        }

        plan.PreviousFileId = previous?.Id;
        if (previous is not null) plan.PreviousEpisodeTitle = MediaNameParser.Parse(previous.Path).EpisodeTitle;
    }

    /// <summary>"Show Episode 054 Nightmare" without "Show" -> "Episode 054 Nightmare"; unchanged when nothing would be left.</summary>
    private static string WithoutPrefix(string name, string? prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix) || !name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return name;
        if (name.Length > prefix.Length && char.IsLetterOrDigit(name[prefix.Length])) return name; // "Showtime" is not "Show"
        var rest = name[prefix.Length..].TrimStart(' ', '-', '.', '_', ':');
        return rest.Length > 0 ? rest : name;
    }

    private static string EpisodeLabel(int season, int episode) => $"S{season:00}E{episode:00}";

    /// <summary>First episode absent between two consecutive files. Specials (season 0) have no reliable numbering.</summary>
    private static string? MissingBefore(int season, int episode, int nextSeason, int nextEpisode)
    {
        if (season == 0 || nextSeason == 0) return null;
        if (nextSeason == season) return nextEpisode > episode + 1 ? EpisodeLabel(season, episode + 1) : null;
        if (nextSeason > season + 1) return $"S{season + 1:00}";
        return nextEpisode > 1 ? EpisodeLabel(nextSeason, 1) : null;
    }

    /// <summary>The item's name without release tags ("Show.S01.1080p" -> "Show"); a plain name stays as typed.</summary>
    public static string GetPlayerTitle(this MediaItem item)
    {
        var title = item.GetDisplayTitle();
        var parsed = MediaNameParser.Parse(title);
        return MediaTitle.HasReleaseTags(parsed) &&!string.IsNullOrWhiteSpace(parsed.Title) ? parsed.Title : title;
    }

    /// <summary>Title in the players: the library item's name, for other files the name parsed from the file name.</summary>
    public static string GetPlayerTitle(this MediaFile file, MediaItem? item)
    {
        if (item is not null) return item.GetPlayerTitle();
        var parsed = MediaNameParser.Parse(file.Path).Title;
        return string.IsNullOrWhiteSpace(parsed) ? file.GetDisplayTitle() : parsed;
    }

    #region Is played
    public static void RecalculatePlayedFiles(this MediaItem item, IList<MediaFile> filesToCalculate, bool cache = true) { 
        foreach(var file in filesToCalculate)
        {
            if (file.Played && !cache) continue;
            if(file.Type == FileType.Folder)
            {
                file.Played = IsFolderPlayed(item, file);
            } else
            {
                file.Played = item.History?.Any(h => h.FileId == file.Id) ?? false;
            }
        }
    }

    private static bool IsFolderPlayed(MediaItem item, MediaFile folder) //TODO this can be optimised, but not too complex so whatever
    {
        if (item.History is null) return false;
        var folderFiles = item.Files.GetFilesForDirectory(folder.Path);

        foreach (var file in folderFiles)
        {
            if (file.Type == FileType.Folder)
            {
                // If one nested folder is not played, the parent folder is not played.
                if (!IsFolderPlayed(item, file))
                {
                    return false;
                }
            }
            else
            {
                // If one file is not played, the folder is not played.
                if (!item.History.Any(h => h.FileId == file.Id))
                {
                    return false;
                }
            }
        }
        // If we get here, it means all nested folders and files are played.
        return true;
    }
    #endregion

    public static List<FolderLoadedNavigation> BuildNavigationPath(this MediaItem item, string topFolder)
    {
        //TODO no validation that provided topFolder is subpath of parent
        var navigation = new List<FolderLoadedNavigation>();
        var rootDrive = Path.GetPathRoot(topFolder);
        var currentFolder = topFolder;
        while(currentFolder != item.FolderDirectory && currentFolder != rootDrive)
        {
            navigation.Add(new FolderLoadedNavigation()
            {
                Title = Path.GetFileName(currentFolder),
                Path = currentFolder
            });
            currentFolder = Path.GetDirectoryName(currentFolder)!;
        }
        navigation.Reverse();
        return navigation;
    }
    public static List<MediaFile> GetFilesForDirectory(this IList<MediaFile> files, string rootDirectory)
    {
        var resultFiles = files
            .Where(file => Path.GetDirectoryName(file.Path) == rootDirectory)
            .OrderByDescending(x => x.Type)
            .ThenBy(x => x.Title)
            .ToList();

        return resultFiles;
    }

    //Immediate sibling
    private static MediaFile FindNextMediaFileSibling(string currentFolder, IList<MediaFile> files, string lastItemPath)
    {
        var currentFiles = files.GetFilesForDirectory(currentFolder)
           .Where(i => i.Type == FileType.File)
           .OrderBy(i => i.Title)
           .ToList();

        var currentFileIndex = currentFiles.IndexOf(currentFiles.FirstOrDefault(m => m.Path == lastItemPath));
        if (currentFileIndex == -1)
        {
            throw new Exception("Couldn't find next file to play. History data differs from current file data");
        }

        //Jump to next file in current folder
        if (currentFileIndex < currentFiles.Count - 1)
        {
            return currentFiles[currentFileIndex + 1];
        }
        return null;
    }

    private static MediaFile FindNextMediaFileInNewDirectory(string currentFolder, IList<MediaFile> files, IList<string> history)
    {
        var currentFiles = files.GetFilesForDirectory(currentFolder)
           .Where(i => i.Type == FileType.File)
           .OrderBy(i => i.Title)
           .ToList();

        if (currentFiles.Count > 0 && history is not null)
        {
            currentFiles = currentFiles
                .Where(i => !history.Contains(i.Path))
                .ToList();
        }
        return currentFiles.FirstOrDefault();
    }

    private static MediaFile FindInParent(string directory, IList<MediaFile> files, IList<string> history)
    {
        var parentDir = Directory.GetParent(directory).FullName;
        var allParentFiles = files.GetFilesForDirectory(parentDir);
        var result = FindNextMediaFileInNewDirectory(parentDir, files, history);
        if (result is not null)
        {
            return result;
        }

        var parentFolders = allParentFiles
            .Where(i => i.Type == FileType.Folder)
            .OrderBy(i => i.Title)
            .ToList();

        var currentFolderIndex = parentFolders.IndexOf(parentFolders.FirstOrDefault(m => m.Path == directory));
        if (currentFolderIndex >= parentFolders.Count - 1)
        {
            return null; //Last one
        }

        for (var i = currentFolderIndex + 1; i < parentFolders.Count; i++)
        {
            var folderFile = FindNextMediaFileInSubdirectories(parentFolders[i].Path, files, history);
            if (folderFile is not null)
            {
                return folderFile;
            }
        }
        return null;
    }

    private static MediaFile FindNextMediaFileInSubdirectories(string directory, IList<MediaFile> files, IList<string> history)
    {
        var allFiles = files.GetFilesForDirectory(directory);

        if (history is not null)
        {
            var dirFiles = allFiles
                .Where(f => f.Type == FileType.File && !history.Contains(f.Path))
                .OrderBy(f => f.Title)
                .ToList();
            if (dirFiles.Any())
            {
                return dirFiles.First();
            }
        }

        var subdirectories = allFiles
            .Where(f => f.Type == FileType.Folder)
            .ToList();
        foreach (var subdirectory in subdirectories)
        {
            List<MediaFile> filesInSubdirectory = files.GetFilesForDirectory(subdirectory.Path)
                .Where(f => f.Type == FileType.File)
                .OrderBy(f => f.Title)
                .ToList();
            if (filesInSubdirectory.Count > 0)
            {
                return filesInSubdirectory.First();
            }
            else
            {
                MediaFile nextFile = FindNextMediaFileInSubdirectories(subdirectory.Path, files, history);
                if (nextFile != null)
                {
                    return nextFile;
                }
            }
        }
        return null;
    }
}