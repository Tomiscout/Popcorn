using VideoBlazor.Domain.Enum;
using VideoBlazor.Domain.Helpers;
using VideoBlazor.Domain.Models;

namespace VideoPlayer.Domain.Extensions;

public static class MediaItemExtensions
{
    public static MediaFile GetNextMediaFile(this MediaItem item)
    {
        MediaFile result = null;

        var lastItem = item.History?.LastOrDefault();
        if (lastItem is null)
        {
            result = FindNextMediaFileInNewDirectory(item.FolderDirectory, item.Files, null);
            result ??= FindNextMediaFileInSubdirectories(item.FolderDirectory, item.Files, null);
            return result;
        }

        //If next file is sibling
        var currentFolder = Directory.GetParent(lastItem.Filename).FullName;
        result ??= FindNextMediaFileSibling(currentFolder, item.Files, lastItem.Filename);
        result ??= FindNextMediaFileInSubdirectories(currentFolder, item.Files, item.History.Select(h => h.Filename).ToList());
        if (result is not null)
        {
            return result;
        }

        //Moves to parent if not found yet.
        //When going to parent, shouldn't play already played directories
        while (currentFolder != item.FolderDirectory) //Test, may be dangerous
        {
            result = FindInParent(currentFolder, item.Files, item.History?.Select(i => i.Filename).ToList());
            if (result is not null)
            {
                return result;
            }
            currentFolder = Directory.GetParent(currentFolder).FullName;
        }

        return result;
    }

    //Immediate sibling
    private static MediaFile FindNextMediaFileSibling(string currentFolder, IList<MediaFile> files, string lastItemPath)
    {
        var currentFiles = MediaItemHelper.GetFilesForDirectory(currentFolder, files)
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
        var currentFiles = MediaItemHelper.GetFilesForDirectory(currentFolder, files)
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
        var allParentFiles = MediaItemHelper.GetFilesForDirectory(parentDir, files);
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
        var allFiles = MediaItemHelper.GetFilesForDirectory(directory, files);

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
            List<MediaFile> filesInSubdirectory = MediaItemHelper.GetFilesForDirectory(subdirectory.Path, files)
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