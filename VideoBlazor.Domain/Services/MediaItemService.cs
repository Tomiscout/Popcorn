using VideoBlazor.Domain.Models;
using VideoBlazor.Domain.Enum;
using VideoBlazor.Domain;
using VideoBlazor.Domain.Interfaces;

namespace VideoBlazor.Core.Services
{
	public class MediaItemService
	{
        private readonly IMediaItemRepository _mediaItemRepository;

        public MediaItemService(IMediaItemRepository mediaItemRepository)
        {
            _mediaItemRepository = mediaItemRepository;
        }

        public async Task CreateMediaItemFromScan(MediaItem item, MediaScanResult scanResult)
        {
            item.Files = GetFilesFromScanResult(scanResult);
            await _mediaItemRepository.AddMediaItem(item);
        }

        public List<MediaFile> GetFilesFromScanResult(MediaScanResult scanResult)
        {
            var files = new List<MediaFile>();
            foreach (var path in scanResult.Files)
            {
                files.Add(new MediaFile
                {
                    Id = Guid.NewGuid(),
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
            await _mediaItemRepository.SaveMediaItem(item);

            return item;
        }
    }
}
