using System.Diagnostics;
using Popcorn.Application.Extensions;
using Popcorn.Domain;
using Popcorn.Domain.Enum;
using Popcorn.Domain.Interfaces;
using Popcorn.Domain.Models;

namespace Popcorn.Application.Services;

// No description provider in the player, which never creates items.
public class MediaItemService(IMediaItemRepository mediaItemRepository, IMediaFileRepository mediaFileRepository, IMediaDescriptionProvider? descriptions = null)
{
    /// <summary>New library item (folder add, downloads); its title is cleaned of release tags (<see cref="MediaTitle.FromName"/>).</summary>
    public async Task<Guid> CreateMediaItemFromScan(MediaItem item, MediaScanResult scanResult)
    {
        if (descriptions != null) item.Description ??= await descriptions.FindDescriptionAsync(item.Title, item.Source); // the raw name still has the year
        item.Title = MediaTitle.FromName(item.Title);
        var newItemId = await mediaItemRepository.CreateMediaItem(item);
        await mediaFileRepository.UpsertMediaFiles(item.Id, GetFilesFromScanResult(scanResult));
        return newItemId;
    }

    /// <summary>Adds scanned files (and their folders) to an item, keeping the ones it has.</summary>
    public async Task AddFilesFromScan(Guid itemId, MediaScanResult scanResult)
    {
        var existing = (await mediaFileRepository.GetFilesForItemId(itemId)).Select(f => f.Path).ToHashSet();
        foreach (var file in GetFilesFromScanResult(scanResult).Where(f => !existing.Contains(f.Path)))
        {
            file.ItemId = itemId;
            await mediaFileRepository.InsertMediaFile(file);
        }
    }

    /// <summary>Drops an item's files (and folders) inside <paramref name="folder"/>, e.g. a deleted download.</summary>
    public async Task RemoveFilesUnder(Guid itemId, string folder)
    {
        var root = Path.TrimEndingDirectorySeparator(folder);
        var files = await mediaFileRepository.GetFilesForItemId(itemId);
        var kept = files.Where(f => f.Path is null || (f.Path != root && !f.Path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))).ToList();
        if (kept.Count != files.Count) await mediaFileRepository.UpsertMediaFiles(itemId, kept, false);
    }

    /// <summary>
    /// Deletes an item's files from disk, then its folders (deepest first, item folder last) that are left empty.
    /// Folders still holding anything the library does not know about are kept. Returns paths that could not be deleted.
    /// </summary>
    public async Task<List<string>> DeleteItemFilesFromDisk(MediaItem item)
    {
        var files = await mediaFileRepository.GetFilesForItemId(item.Id);
        var failed = new List<string>();
        foreach (var path in files.Where(f => f.Type == FileType.File && !string.IsNullOrEmpty(f.Path)).Select(f => f.Path!))
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception) { failed.Add(path); }
        }

        var folders = files.Where(f => f.Type == FileType.Folder && !string.IsNullOrEmpty(f.Path)).Select(f => f.Path!)
            .Append(item.FolderDirectory)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => Path.TrimEndingDirectorySeparator(Path.GetFullPath(p)))
            .Where(p => Path.GetPathRoot(p) != p)
            .Distinct()
            .OrderByDescending(p => p.Length);
        foreach (var folder in folders)
        {
            try { if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder); }
            catch (Exception) { failed.Add(folder); }
        }
        return failed;
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
        
        allFiles.AddRange(scanResult.Files.Select(path =>
        {
            var file = new MediaFile
            {
                Id = Guid.NewGuid(),
                Path = path,
                Title = Path.GetFileName(path),
                Type = FileType.File
            };
            file.ParseTags();
            return file;
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

        // Parse the names of the files it already had again, so parser improvements reach them.
        foreach (var file in (await mediaFileRepository.GetFilesForItemId(item.Id)).Where(f => f.Type == FileType.File))
        {
            file.ParseTags();
            await mediaFileRepository.UpdateMediaFile(file);
        }
        sw.Stop();
        return item;
    }
}