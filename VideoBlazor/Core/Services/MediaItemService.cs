using Microsoft.Maui.Graphics;
using VideoBlazor.Core.Enums;
using VideoBlazor.Core.Model;
using VideoBlazor.Persistence;
using static MudBlazor.CategoryTypes;

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

        public List<MediaFile> GetFilesForDirectory(string rootDirectory, IList<MediaFile> files)
        {
            var folders = new HashSet<string>();

            var result = new List<MediaFile>();
            foreach(var file in files)
            {
                var parent = Path.GetDirectoryName(file.Path);
                var grandParent = Path.GetDirectoryName(parent);
                if (parent == rootDirectory) {
                    result.Add(file);
                } else if(grandParent == rootDirectory)
                {
                    folders.Add(parent);
                    
                }
            }

            foreach(var folder in folders)
            {
                result.Add(new MediaFile
                {
                    Path = folder,
                    Title = folder.Substring(folder.LastIndexOf("\\") + 1), //Windows dependant
                    Type = FileType.Folder
                });
            }

            return result.OrderByDescending(x => x.Type).ThenBy(x => x.Title).ToList();
        }
    }
}
