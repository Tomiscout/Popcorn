using System.Diagnostics;
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

    private List<MediaFile> GetFilesFromScanResult(MediaScanResult? scanResult)
    {
        if (scanResult == null) return [];
        
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

    public async Task<MediaItem> RescanItem(Guid itemId)
    {
        var sw = new Stopwatch();
        sw.Start();
        var item = await mediaItemRepository.GetMediaItem(itemId);
        var scanResult = await MediaLoader.ScanDirectorySingle(item.FolderDirectory);

        await mediaFileRepository.UpsertMediaFiles(item.Id, GetFilesFromScanResult(scanResult), false);
        sw.Stop();
        return item;
    }
}