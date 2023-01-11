using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
    }
}
