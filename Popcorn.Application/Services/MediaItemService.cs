using Popcorn.Domain;
using Popcorn.Domain.Enum;
using Popcorn.Domain.Interfaces;
using Popcorn.Domain.Models;

namespace Popcorn.Application.Services;

public class MediaItemService(IMediaItemRepository mediaItemRepository, IMediaFileRepository mediaFileRepository)
{
    public async Task<Guid> CreateMediaItemFromScan(MediaItem item, MediaScanResult scanResult)
    {
        var newItemId = await mediaItemRepository.CreateMediaItem(item);
        await mediaFileRepository.UpsertMediaFiles(item.Id, GetFilesFromScanResult(scanResult));
        return newItemId;
    }

    private List<MediaFile> GetFilesFromScanResult(MediaScanResult scanResult)
    {
        var allFiles = scanResult.Folders.Select(path => new MediaFile
        {
            Id = Guid.NewGuid(),
            Path = path,
            Title = Path.GetFileName(path),
            Type = FileType.Folder
        })
        .ToList();
        
        allFiles.AddRange(scanResult.Files.Select(path => new MediaFile
        {
            Id = Guid.NewGuid(),
            Path = path,
            Title = Path.GetFileName(path),
            Type = FileType.File
        }));

        return allFiles;
    }

    public async Task<MediaItem> RescanItem(MediaItem item)
    {
        var scanResult = await MediaLoader.ScanFolder(item.FolderDirectory);
            
        var files = scanResult.SelectMany(GetFilesFromScanResult).ToList();
        await mediaFileRepository.UpsertMediaFiles(item.Id, files);
        item.Files = files;

        return item;
    }
}