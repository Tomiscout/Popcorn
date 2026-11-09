using Popcorn.Domain.Models;

namespace Popcorn.Domain;
//With given folder scan media and subfolders, group folders
public static class MediaLoader
{
    public static readonly string[] VideoFormats = { ".mkv", ".mp4", ".m4v", ".mpeg", ".avi", ".wmv", ".vp6", ".vp7", ".webm", ".amc", ".psv",
        ".3gp", ".av1", ".hdmov", ".ogv", ".vc1", ".mp4v", ".mpeg4", ".mpg", ".mov", ".vid", ".h264", ".mp2v", ".m1v", ".xvid", ".divx", ".m2v", ".m2ts", ".streamdump", ".ts",
     ".vp5", ".yuv", ".pmf", ".mxf", ".tp", ".trp", ".gxf", ".flv", ".302", ".dv", ".avs", ".y4m"};

    public static readonly string[] SubtitleFormats = { ".srt", ".sub", ".ssa", ".ass", ".vtt", ".idx", ".rt", ".smil", ".smi" };

    public static async Task<IList<MediaScanResult>> ScanFolder(string folder, List<string>? ignoreFolders = null)
    {
        var resultList = new List<MediaScanResult>();
        string[] rootDirectories;

        try
        {
            if (!Directory.Exists(folder)) return resultList;

            rootDirectories = Directory.GetDirectories(folder);
        }
        catch (Exception e)
        {
            //TODO Log
            //happends on rescan folder, when it doesnt exist anymore
            return resultList;
        }

        //Dirs
        foreach (var rootDirectory in rootDirectories)
        {
            if (ignoreFolders != null && ignoreFolders.Contains(rootDirectory)) continue;

            var allPaths = Directory.EnumerateFiles(rootDirectory, "*.*", SearchOption.AllDirectories).ToList();
            var scanResult = new MediaScanResult
            {
                FolderPath = rootDirectory,
                Title = Path.GetFileName(rootDirectory),
                Files = new List<string>(),
            };

            foreach (var format in VideoFormats)
            {
                var resultPaths = allPaths.Where(p => p.ToLower().EndsWith(format));
                scanResult.Files.AddRange(resultPaths.ToList());
            }

            if (scanResult.Files.Count > 0)
            {
                resultList.Add(scanResult);
            }
        }

        //Files
        var allFiles = Directory.GetFiles(folder);
        foreach (var file in allFiles)
        {
            foreach (var format in VideoFormats)
            {
                if (file.EndsWith(format))
                {
                    resultList.Add(new MediaScanResult
                    {
                        FolderPath = folder,
                        Title = Path.GetFileName(file),
                        Files = new List<string>() { file },
                    });
                    break;
                }
            }
        }

        return resultList.OrderBy(r => r.Files.Count).Reverse().ToList();
    }
    
    public static async Task<MediaScanResult?> ScanDirectorySingle(string folder)
    {
        if (!Directory.Exists(folder)) 
            return null;

        var allEntries = Directory
            .EnumerateFileSystemEntries(folder, "*", SearchOption.AllDirectories)
            .ToList();

        var folderEntries = allEntries
            .Where(Directory.Exists)
            .ToList();
        
        var mediaFiles = allEntries
            .Where(p => VideoFormats.Any(ext => p.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
            //.Where(p => folderEntries.Any(f => f != p)) // Removes folders with extension names 
            .ToList();

        if (mediaFiles.Count == 0) 
            return null;

        return new MediaScanResult
        {
            FolderPath = folder,
            Title      = Path.GetFileName(folder),
            Files      = mediaFiles,
            Folders    = folderEntries
        };
    }
}