using Popcorn.Domain.Models;
using Popcorn.Domain.Enum;
using Popcorn.Domain;
using Popcorn.Domain.Interfaces;

namespace Popcorn.Core.Services
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
            await _mediaItemRepository.CreateMediaItem(item);
            await _mediaItemRepository.UpsertMediaFiles(item.Id, GetFilesFromScanResult(scanResult));
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
            
            var files = scanResult.SelectMany(GetFilesFromScanResult).ToList();
            await _mediaItemRepository.UpsertMediaFiles(item.Id, files);
            item.Files = files;

            return item;
        }
    }
}
