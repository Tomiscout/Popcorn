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
    /// <summary>
    /// New library item (folder add, downloads); its title is cleaned of release tags (<see cref="MediaTitle.FromName"/>).
    /// Its files get <paramref name="source"/> (default: a folder source of the scanned folder).
    /// </summary>
    public async Task<Guid> CreateMediaItemFromScan(MediaItem item, MediaScanResult scanResult, ItemSource? source = null)
    {
        if (descriptions != null) item.Description ??= await descriptions.FindDescriptionAsync(item.Title, item.Source); // the raw name still has the year
        item.Title = MediaTitle.FromName(item.Title);
        var newItemId = await mediaItemRepository.CreateMediaItem(item);

        source ??= new ItemSource
        {
            Kind = ItemSourceKind.Folder,
            Title = FolderTitle(scanResult.FolderPath ?? item.FolderDirectory, item.Title),
            RootPath = scanResult.FolderPath ?? item.FolderDirectory,
        };
        source.ItemId = newItemId;
        if (string.IsNullOrWhiteSpace(source.RootPath)) source.RootPath = item.FolderDirectory;
        await mediaItemRepository.InsertSource(source);
        item.Sources = [source];

        var files = GetFilesFromScanResult(scanResult);
        foreach (var file in files) file.SourceId = source.Id;
        await mediaFileRepository.UpsertMediaFiles(item.Id, files);
        return newItemId;
    }

    private static string FolderTitle(string? folder, string fallback)
    {
        var name = string.IsNullOrWhiteSpace(folder) ? null : Path.GetFileName(Path.TrimEndingDirectorySeparator(folder));
        return string.IsNullOrWhiteSpace(name) ? fallback : name;
    }

    /// <summary>Adds scanned files (and their folders) to an item, keeping the ones it has; new ones get <paramref name="sourceId"/>.</summary>
    public async Task AddFilesFromScan(Guid itemId, MediaScanResult scanResult, Guid? sourceId = null)
    {
        var existing = (await mediaFileRepository.GetFilesForItemId(itemId)).Select(f => f.Path).ToHashSet();
        foreach (var file in GetFilesFromScanResult(scanResult).Where(f => !existing.Contains(f.Path)))
        {
            file.ItemId = itemId;
            file.SourceId = sourceId;
            await mediaFileRepository.InsertMediaFile(file);
        }
    }

    /// <summary>
    /// The source of a download in an item, made or updated here (also for downloads from before sources existed):
    /// <paramref name="sourceId"/> when it still exists (a batch's), else the source rooted at the download's folder,
    /// else a new one, which takes over the item's files in that folder. The download is added to its origins once.
    /// </summary>
    public async Task<ItemSource> EnsureDownloadSourceAsync(Guid itemId, Guid? sourceId, string savePath, string title, SourceOrigin origin)
    {
        var root = Path.TrimEndingDirectorySeparator(savePath);
        var sources = await mediaItemRepository.GetSources(itemId);
        var source = sources.FirstOrDefault(s => s.Id == sourceId)
                     ?? sources.FirstOrDefault(s => Path.TrimEndingDirectorySeparator(s.RootPath) == root);
        var created = source is null;
        source ??= new ItemSource { ItemId = itemId, Kind = ItemSourceKind.Download, Title = title, RootPath = root, AddedAt = origin.AddedAt };

        var changed = false;
        if (source.Kind != ItemSourceKind.Download)
        {
            // An item made from the download (its folder = the download's): the folder source becomes its download source.
            source.Kind = ItemSourceKind.Download;
            changed = true;
        }
        if (!source.Origins.Any(o => o.DownloadId is not null && o.DownloadId == origin.DownloadId))
        {
            source.Origins.Add(origin);
            changed = true;
        }

        if (created)
        {
            await mediaItemRepository.InsertSource(source);
            // Older attached downloads were saved inside the item's folder and their files belong to its folder source.
            await mediaItemRepository.MoveFilesToSource(itemId, source.Id, root);
        }
        else if (changed) await mediaItemRepository.UpdateSource(source);
        return source;
    }

    /// <summary>An empty download source with its own folder (a batch of downloads into an item).</summary>
    public async Task<ItemSource> CreateSourceAsync(Guid itemId, ItemSourceKind kind, string title, string rootPath)
    {
        var source = new ItemSource { ItemId = itemId, Kind = kind, Title = title, RootPath = Path.TrimEndingDirectorySeparator(rootPath) };
        await mediaItemRepository.InsertSource(source);
        return source;
    }

    /// <summary>
    /// Merges <paramref name="otherId"/> into <paramref name="targetId"/>: its sources (each a virtual folder of the target
    /// from now on), files, history and intro/outro references move, the item is deleted. The target takes its pictures,
    /// description and media source where it has none. Returns the merged item as it was (images still to delete:
    /// those the target did not take), null when either item does not exist.
    /// </summary>
    public async Task<MediaItem?> MergeAsync(Guid targetId, Guid otherId)
    {
        if (targetId == otherId) throw new ArgumentException("An item cannot be merged into itself");
        var target = await mediaItemRepository.GetMediaItem(targetId);
        var other = await mediaItemRepository.GetMediaItemWithSegments(otherId);
        if (target is null || other is null) return null;

        // Items from before sources existed have one in the DB already (migration); new ones get theirs on creation.
        await mediaItemRepository.MergeItems(targetId, otherId, other.GetDisplayTitle());
        if (string.IsNullOrEmpty(target.ImagePath) && !string.IsNullOrEmpty(other.ImagePath))
        {
            await mediaItemRepository.SetImagePath(targetId, MediaImageKind.Poster, other.ImagePath);
            other.ImagePath = null;
        }
        if (string.IsNullOrEmpty(target.BackdropPath) && !string.IsNullOrEmpty(other.BackdropPath))
        {
            await mediaItemRepository.SetImagePath(targetId, MediaImageKind.Backdrop, other.BackdropPath);
            other.BackdropPath = null;
        }
        await mediaItemRepository.SetMissingDetails(targetId, other.Description, other.Source);
        return other;
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
        // Source folders too: downloads attached to the item and merged items live outside its folder.
        var sourceRoots = (await mediaItemRepository.GetSources(item.Id)).Select(s => s.RootPath);
        return DeleteFromDisk(files, sourceRoots.Append(item.FolderDirectory));
    }

    /// <summary>
    /// Removes a source from an item: its files leave the library (with their history), with
    /// <paramref name="deleteFiles"/> also the disk (folders left empty too). The item's last source cannot be removed
    /// (remove the item instead). Removing the source of the item's folder moves the item to the oldest other source's
    /// folder. Returns paths that could not be deleted.
    /// </summary>
    public async Task<List<string>> RemoveSourceAsync(Guid itemId, Guid sourceId, bool deleteFiles)
    {
        var item = await mediaItemRepository.GetMediaItemWithFilesAndHistory(itemId) ?? throw new ArgumentException("The library item no longer exists");
        var sources = item.Sources ?? [];
        var source = sources.FirstOrDefault(s => s.Id == sourceId) ?? throw new ArgumentException("The item has no such source");
        if (sources.Count <= 1) throw new InvalidOperationException("The item's only source cannot be removed; remove the item instead");

        // Owner as the tree sees it (files of older rows fall back to the item folder's source).
        var files = item.Files.Where(f => !f.IsVirtual && item.SourceOf(f)?.Id == sourceId).ToList();
        var failed = deleteFiles ? DeleteFromDisk(files, [source.RootPath]) : [];

        var root = Path.TrimEndingDirectorySeparator(item.FolderDirectory ?? "");
        var newFolder = Path.TrimEndingDirectorySeparator(source.RootPath) == root
            ? sources.Where(s => s.Id != sourceId).OrderBy(s => s.AddedAt).First().RootPath
            : null;
        await mediaItemRepository.RemoveSource(itemId, sourceId, files.Select(f => f.Id).ToList(), newFolder);
        return failed;
    }

    /// <summary>Deletes the files, then the given and the files' folders left empty (deepest first).</summary>
    private static List<string> DeleteFromDisk(IReadOnlyCollection<MediaFile> files, IEnumerable<string?> extraFolders)
    {
        var failed = new List<string>();
        foreach (var path in files.Where(f => f.Type == FileType.File && !string.IsNullOrEmpty(f.Path)).Select(f => f.Path!))
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception) { failed.Add(path); }
        }

        var folders = files.Where(f => f.Type == FileType.Folder && !string.IsNullOrEmpty(f.Path)).Select(f => f.Path!)
            .Concat(extraFolders.OfType<string>())
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

    /// <summary>
    /// Scans the folders of the item's sources again: new videos are added to the source whose folder holds them (the
    /// deepest one), files gone from a folder that still exists are dropped. Parses all file names again.
    /// </summary>
    public async Task<MediaItem> RescanItem(Guid itemId)
    {
        var sw = new Stopwatch();
        sw.Start();
        var item = await mediaItemRepository.GetMediaItem(itemId);
        var sources = await mediaItemRepository.GetSources(itemId);
        var roots = sources.Select(s => (Source: s, Root: Path.TrimEndingDirectorySeparator(s.RootPath)))
            .Where(r => r.Root.Length > 0)
            .OrderByDescending(r => r.Root.Length)
            .ToList();
        if (roots.Count == 0 && !string.IsNullOrWhiteSpace(item.FolderDirectory))
            roots.Add((null!, Path.TrimEndingDirectorySeparator(item.FolderDirectory)));

        var existing = await mediaFileRepository.GetFilesForItemId(item.Id);
        var known = existing.Select(f => f.Path).ToHashSet();
        var found = new HashSet<string>();
        var scanned = new List<string>();
        foreach (var root in roots.Select(r => r.Root).Distinct())
        {
            if (!Directory.Exists(root)) continue; // unplugged drive, deleted download: keep what the library knows
            scanned.Add(root);
            var scan = await MediaLoader.ScanDirectorySingle(root);
            foreach (var file in GetFilesFromScanResult(scan))
            {
                if (!found.Add(file.Path) || known.Contains(file.Path)) continue;
                file.ItemId = item.Id;
                file.SourceId = roots.FirstOrDefault(r => MediaTree.IsUnder(file.Path, r.Root)).Source?.Id;
                await mediaFileRepository.InsertMediaFile(file);
                known.Add(file.Path);
            }
        }

        var gone = existing.Where(f => !found.Contains(f.Path) && scanned.Any(root => MediaTree.IsUnder(f.Path, root))).ToList();
        if (gone.Count > 0)
        {
            var goneIds = gone.Select(f => f.Id).ToHashSet();
            await mediaFileRepository.UpsertMediaFiles(item.Id, existing.Where(f => !goneIds.Contains(f.Id)).ToList(), false);
        }

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