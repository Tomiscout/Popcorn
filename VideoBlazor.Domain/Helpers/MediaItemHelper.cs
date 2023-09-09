using VideoBlazor.Domain.Enum;
using VideoBlazor.Domain.Models;

namespace VideoBlazor.Domain.Helpers;

public static class MediaItemHelper
{
    public static List<MediaFile> GetFilesForDirectory(string rootDirectory, IList<MediaFile> files)
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
                Path = folder,
                Title = folder.Substring(folder.LastIndexOf("\\") + 1), //Windows dependant
                Type = FileType.Folder
            });
        }

        var result = resultFiles.OrderByDescending(x => x.Type);
        return result.ToList();
    }
}
