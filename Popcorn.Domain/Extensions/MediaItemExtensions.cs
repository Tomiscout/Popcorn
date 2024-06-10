using Popcorn.Domain.Enum;
using Popcorn.Domain.Models;
using Popcorn.Shared.Utils;

namespace PopcornPlayer.Domain.Extensions;

public static class MediaItemExtensions
{
    public static MediaFile GetNextMediaFile(this MediaItem item, IList<ItemHistory> history = null!)
    {
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
        var currentFolder = Directory.GetParent(lastItem.Filename).FullName;
        result ??= FindNextMediaFileSibling(currentFolder, item.Files, lastItem.Filename);
        result ??= FindNextMediaFileInSubdirectories(currentFolder, item.Files, history.Select(h => h.Filename).ToList());
        if (result is not null)
        {
            return result;
        }

        //Moves to parent if not found yet.
        //When going to parent, shouldn't play already played directories
        while (currentFolder != item.FolderDirectory) //Test, may be dangerous
        {
            result = FindInParent(currentFolder, item.Files, history?.Select(i => i.Filename).ToList());
            if (result is not null)
            {
                return result;
            }
            currentFolder = Directory.GetParent(currentFolder).FullName;
        }

        return result;
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
                file.Played = item.History?.Any(h => h.Filename == file.Path) ?? false;
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
                if (!item.History.Any(h => h.Filename == file.Path))
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
        var foldersSet = new HashSet<string>();

        var resultFiles = new List<MediaFile>();
        foreach (var file in files)
        {
            var parent = Path.GetDirectoryName(file.Path);
            var grandParent = Path.GetDirectoryName(parent);
            if (parent == rootDirectory)
            {
                resultFiles.Add(file);
            }
            else if (grandParent == rootDirectory && !string.IsNullOrWhiteSpace(parent))
            {
                foldersSet.Add(parent);
            }
        }

        var folders = foldersSet.ToList().OrderBy(i => i);
        foreach (var folder in folders)
        {
            resultFiles.Add(new MediaFile
            {
                Id = Guid.NewGuid(),
                Path = folder,
                Title = folder.Substring(folder.LastIndexOf(Path.DirectorySeparatorChar) + 1),
                Type = FileType.Folder
            });
        }

        var result = resultFiles.OrderByDescending(x => x.Type).ToList();
        foreach(var file in result) //TODO its ugly but works
        {
            file.ParseTitle();
        }
        return result;
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