using Microsoft.Maui.Graphics;
using VideoBlazor.Core.Enums;
using VideoBlazor.Core.Model;

namespace VideoBlazor.Core.Services
{
	public class MediaItemService
	{
        private static readonly string _jsonName = "mediaItems.json";


        public async Task<IList<MediaItem>> LoadMediaItems()
        {
            try
            {
                return await JsonUtils.LoadJsonAsync<IList<MediaItem>>(_jsonName);
            }
            catch (FileNotFoundException)
            {
                var emptyList = new List<MediaItem>();
                await JsonUtils.SaveJson(emptyList, _jsonName);
                return emptyList;
            }
        }

        public async Task<MediaItem> LoadItemById(string id)
        {
            return (await LoadMediaItems()).FirstOrDefault(n => n.Id.ToString() == id);
        }

        public async Task AddMediaItem(MediaItem mediaItem)
        {
            mediaItem.Id = Guid.NewGuid();
            if (!string.IsNullOrWhiteSpace(mediaItem.ImagePath))
            {
                ImageUtils.SaveImage(mediaItem.ImagePath, mediaItem.Id.ToString());
            }

            var items = await LoadMediaItems();
            items.Add(mediaItem);
            await JsonUtils.SaveJson(items, _jsonName);
        }

        public async Task DeleteMediaItem(Guid id)
        {
            var items = await LoadMediaItems();
            items.Remove(items.FirstOrDefault(x => x.Id == id));
            await JsonUtils.SaveJson(items, _jsonName);
        }

        public async Task CreateMediaItemFromScan(MediaItem item, MediaScanResult scanResult)
        {
            foreach (var path in scanResult.Files)
            {
                item.Files.Add(new MediaFile
                {
                    Path = path,
                    Title = Path.GetFileName(path),
                    Type = FileType.File
                });
            }
            await AddMediaItem(item);
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
