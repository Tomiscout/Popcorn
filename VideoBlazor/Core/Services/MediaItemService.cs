using VideoBlazor.Core.Enums;
using VideoBlazor.Core.Model;
using VideoBlazor.Persistence;
using VideoBlazor.Domain.Models;
using VideoBlazor.Domain.Enum;
using static MudBlazor.CategoryTypes;
using System.IO;

namespace VideoBlazor.Core.Services
{
	public class MediaItemService
	{
        private static readonly string _jsonFormat = "mediaItems-{0}.json";

        private readonly JsonRepository _jsonRepository;
        private readonly ImageRepository _imageRepository;

        public MediaItemService(JsonRepository jsonRepository, ImageRepository imageRepository)
        {
            _jsonRepository = jsonRepository;
            _imageRepository = imageRepository;
        }

        public async Task<MediaItem> LoadMediaItem(Guid id) {
            return await LoadMediaItem(id.ToString());
        }
        public async Task<MediaItem> LoadMediaItem(string id)
        {
            var mediaPath = string.Format(_jsonFormat, id);
            return await _jsonRepository.LoadJsonAsync<MediaItem>(mediaPath);
        }

        public async Task SaveMediaItem(MediaItem item)
        {
            await _jsonRepository.SaveJson(item, string.Format(_jsonFormat, item.Id.ToString()));
        }

        public async Task<IList<MediaItem>> LoadMediaItems()
        {
            try
            {
                var search = string.Format(_jsonFormat, "*");
                var allPaths = Directory.EnumerateFiles(_jsonRepository.GetDataFolder(), search, SearchOption.TopDirectoryOnly).ToList();
                var result = new List<MediaItem>();
                foreach (var path in allPaths) {
                    result.Add(await _jsonRepository.LoadJsonAsync<MediaItem>(Path.GetFileName(path)));
                }
                return result;
            }
            catch (FileNotFoundException)
            {
                var emptyList = new List<MediaItem>();
                return emptyList;
            }
        }

        public async Task AddMediaItem(MediaItem mediaItem)
        {
            mediaItem.Id = Guid.NewGuid();
            if (!string.IsNullOrWhiteSpace(mediaItem.ImagePath))
            {
                _imageRepository.SaveImage(mediaItem.ImagePath, mediaItem.Id.ToString());
            }

            await SaveMediaItem(mediaItem);
        }

        public async Task AddHistoryItem(Guid id, PlaybackHistory item)
        {
            var mediaItem = await LoadMediaItem(id);
            if(mediaItem.History is null)
            {
                mediaItem.History = new List<PlaybackHistory>();
            }
            mediaItem.History.Add(item);
            await SaveMediaItem(mediaItem);
        }

        public async Task DeleteMediaItem(Guid id)
        {
            var currentItem = await LoadMediaItem(id);
            if(currentItem is not null)
            {
                File.Delete(string.Format(_jsonFormat, id.ToString()));
            }
        }

        public async Task CreateMediaItemFromScan(MediaItem item, MediaScanResult scanResult)
        {
            item.Files = GetFilesFromScanResult(scanResult);
            await AddMediaItem(item);
        }

        public List<MediaFile> GetFilesFromScanResult(MediaScanResult scanResult)
        {
            var files = new List<MediaFile>();
            foreach (var path in scanResult.Files)
            {
                files.Add(new MediaFile
                {
                    Path = path,
                    Title = Path.GetFileName(path),
                    Type = FileType.File
                });
            }
            return files;
        }

        public async Task<MediaItem> RescanItem(MediaItem item)
        {
            var scanResult = await MediaLoader.ScanFolder(item.FolderDirectory);
            
            item.Files = scanResult.SelectMany(GetFilesFromScanResult).ToList();
            await SaveMediaItem(item);

            return item;
        }

        public List<MediaFile> GetFilesForDirectory(string rootDirectory, IList<MediaFile> files, bool flatFiles = false, bool filesOnly = false)
        {
            var foldersSet = new HashSet<string>();

            var resultFiles = new List<MediaFile>();
            foreach(var file in files)
            {
                var parent = Path.GetDirectoryName(file.Path);
                var grandParent = Path.GetDirectoryName(parent);
                if (parent == rootDirectory) {
                    resultFiles.Add(file);
                } else if(!filesOnly && grandParent == rootDirectory)
                {
                    foldersSet.Add(parent);
                }
            }

            var folders = foldersSet.ToList().OrderBy(i => i);
            foreach (var folder in folders)
            {
                if(flatFiles)
                {
                    resultFiles.AddRange(GetFilesForDirectory(folder, files, true, true).OrderBy(i => i.Title));
                    continue;
                }

                resultFiles.Add(new MediaFile
                {
                    Path = folder,
                    Title = folder.Substring(folder.LastIndexOf("\\") + 1), //Windows dependant
                    Type = FileType.Folder
                });
            }

            var result = resultFiles.OrderByDescending(x => x.Type);
            if(flatFiles)
            {
                return result.ThenBy(x => x.Title).ToList();
            }
            return result.ToList();
        }

        public MediaFile GetNextFile(MediaItem item)
        {
            MediaFile result = null;

            var lastItem = item.History?.LastOrDefault();
            if(lastItem is null)
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
            while(currentFolder != item.FolderDirectory) //Test, may be dangerous
            {
                result = FindInParent(currentFolder, item.Files, item.History?.Select(i => i.Filename).ToList());
                if(result is not null)
                {
                    return result;
                }
                currentFolder = Directory.GetParent(currentFolder).FullName;
            }

            return result;
        }

        //Immediate sibling
        private MediaFile FindNextMediaFileSibling(string currentFolder, IList<MediaFile> files, string lastItemPath)
        {
            var currentFiles = GetFilesForDirectory(currentFolder, files)
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

        private MediaFile FindNextMediaFileInNewDirectory(string currentFolder, IList<MediaFile> files, IList<string> history)
        {
            var currentFiles = GetFilesForDirectory(currentFolder, files)
               .Where(i => i.Type == FileType.File)
               .OrderBy(i => i.Title)
               .ToList();

            if(currentFiles.Count > 0 && history is not null)
            {
                currentFiles = currentFiles
                    .Where(i => !history.Contains(i.Path))
                    .ToList();
            }
            return currentFiles.FirstOrDefault();
        }

        private MediaFile FindInParent(string directory, IList<MediaFile> files, IList<string> history)
        {
            var parentDir = Directory.GetParent(directory).FullName;
            var allParentFiles = GetFilesForDirectory(parentDir, files);
            var result = FindNextMediaFileInNewDirectory(parentDir, files, history);
            if(result is not null)
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

        private MediaFile FindNextMediaFileInSubdirectories(string directory, IList<MediaFile> files, IList<string> history)
        {
            var allFiles = GetFilesForDirectory(directory, files);

            if (history is not null)
            {
                var dirFiles = allFiles
                    .Where(f => f.Type == FileType.File && !history.Contains(f.Path))
                    .OrderBy(f => f.Title)
                    .ToList();
                if(dirFiles.Any())
                {
                    return dirFiles.First();
                }
            }

            var subdirectories = allFiles
                .Where(f => f.Type == FileType.Folder)
                .ToList();
            foreach (var subdirectory in subdirectories)
            {
                List<MediaFile> filesInSubdirectory = GetFilesForDirectory(subdirectory.Path, files)
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

        public async Task CleanHistory(MediaItem item)
        {
            item.History = null;
            await SaveMediaItem(item);
        }
    }
}
