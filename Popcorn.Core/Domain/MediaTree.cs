using Popcorn.Domain.Enum;
using Popcorn.Domain.Models;

namespace Popcorn.Domain;

/// <summary>
/// The file tree of an item (<see cref="MediaFile.TreePath"/>). With one source it is the folder on disk. With several
/// (a download attached to the item, items merged) every source becomes a virtual folder inside the item's folder,
/// named after it, holding its files as they are below its <see cref="ItemSource.RootPath"/>; the files stay where they
/// are. Built by the repository whenever an item is loaded with its files and sources.
/// </summary>
public static class MediaTree
{
    /// <summary>Path of the file in the tree: <see cref="MediaFile.TreePath"/>, else (tree not built) its path.</summary>
    public static string Tree(this MediaFile file) => file.TreePath ?? file.Path;

    /// <summary>Not in the tree (folder of a source without videos).</summary>
    public static bool IsHidden(this MediaFile file) => file.TreePath is "";

    public static void Build(MediaItem item)
    {
        if (item.Files is null) return;
        item.Files.RemoveAll(f => f.IsVirtual);
        var root = Trim(item.FolderDirectory ?? string.Empty);
        var sources = item.Sources ?? [];
        var owners = OwnerMap(item, sources, root);

        var used = sources
            .Where(s => item.Files.Any(f => f.Type == FileType.File && owners[f.Id] == s.Id))
            .OrderBy(s => s.AddedAt).ThenBy(s => s.Title)
            .ToList();

        if (used.Count <= 1)
        {
            var only = used.FirstOrDefault();
            foreach (var file in item.Files)
            {
                if (only is not null && owners[file.Id] != only.Id) file.TreePath = "";
                else if (only is not null && IsSourceRoot(file, only)) file.TreePath = "";
                else file.TreePath = only is null || root.Length == 0 || IsUnder(file.Path, root)
                    ? file.Path
                    : Path.Combine(root, Relative(only.RootPath, file.Path));
            }
            HideEmptyFolders(item.Files);
            return;
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var bases = new Dictionary<Guid, string>();
        foreach (var source in used)
        {
            var folder = Path.Combine(root, Unique(FolderName(source.Title), names));
            bases[source.Id] = folder;
            item.Files.Add(new MediaFile
            {
                Id = source.Id,
                ItemId = item.Id,
                Path = source.RootPath,
                Title = source.Title,
                ShortTitle = source.Title,
                Type = FileType.Folder,
                SourceId = source.Id,
                TreePath = folder,
                IsVirtual = true,
                TagsProcessed = true,
            });
        }

        foreach (var file in item.Files.Where(f => !f.IsVirtual))
        {
            var owner = owners[file.Id];
            var source = owner is { } id ? used.FirstOrDefault(s => s.Id == id) : null;
            file.TreePath = source is null || IsSourceRoot(file, source)
                ? ""
                : Path.Combine(bases[source.Id], Relative(source.RootPath, file.Path));
        }
        HideEmptyFolders(item.Files);
    }

    /// <summary>The row of the source's own folder: the source is that folder in the tree already.</summary>
    private static bool IsSourceRoot(MediaFile file, ItemSource source) =>
        file.Type == FileType.Folder && Trim(file.Path) == Trim(source.RootPath);

    /// <summary>Folders without a video anywhere below them (subtitle folders, folders of files given to another source).</summary>
    private static void HideEmptyFolders(List<MediaFile> files)
    {
        var withVideos = new HashSet<string>();
        foreach (var file in files.Where(f => f.Type == FileType.File && !f.IsHidden()))
            for (var dir = Path.GetDirectoryName(file.Tree()); !string.IsNullOrEmpty(dir) && withVideos.Add(dir); dir = Path.GetDirectoryName(dir)) { }
        foreach (var folder in files.Where(f => f.Type == FileType.Folder && !f.IsHidden() && !withVideos.Contains(f.Tree())))
            folder.TreePath = "";
    }

    /// <summary>Source of the file: its own, else (older rows, unknown id) the source of the item's folder or the first one.</summary>
    private static Dictionary<Guid, Guid?> OwnerMap(MediaItem item, List<ItemSource> sources, string root)
    {
        var fallback = sources.FirstOrDefault(s => Trim(s.RootPath) == root) ?? sources.FirstOrDefault();
        var map = new Dictionary<Guid, Guid?>();
        foreach (var file in item.Files)
            map[file.Id] = file.SourceId is { } id && sources.Any(s => s.Id == id) ? id : fallback?.Id;
        return map;
    }

    /// <summary>The source a file belongs to (see <see cref="OwnerMap"/>), null when the item has none.</summary>
    public static ItemSource? SourceOf(this MediaItem item, MediaFile file)
    {
        var sources = item.Sources ?? [];
        return sources.FirstOrDefault(s => s.Id == file.SourceId)
               ?? sources.FirstOrDefault(s => Trim(s.RootPath) == Trim(item.FolderDirectory ?? ""))
               ?? sources.FirstOrDefault();
    }

    /// <summary>Folder on disk that bounds searches next to the file (external subtitles): its source's, else the item's.</summary>
    public static string? RootFolderOf(this MediaItem item, MediaFile file)
    {
        var root = item.SourceOf(file)?.RootPath;
        return !string.IsNullOrWhiteSpace(root) && IsUnder(file.Path, root) ? root : item.FolderDirectory;
    }

    public static bool IsUnder(string path, string folder)
    {
        var root = Trim(folder);
        return root.Length > 0 && (path == root || path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal));
    }

    private static string Relative(string root, string path) =>
        !string.IsNullOrWhiteSpace(root) && IsUnder(path, root) && Trim(path) != Trim(root)
            ? Path.GetRelativePath(Trim(root), path)
            : Path.GetFileName(path);

    private static string Trim(string path) => path.Length == 0 ? path : Path.TrimEndingDirectorySeparator(path);

    /// <summary>A folder name of the source's title: no path separators or characters file systems refuse.</summary>
    public static string FolderName(string title)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(['/', '\\', ':']).ToHashSet();
        var name = new string(title.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim().Trim('.').Trim();
        return name.Length == 0 ? "Files" : name;
    }

    private static string Unique(string name, HashSet<string> taken)
    {
        var candidate = name;
        for (var n = 2; !taken.Add(candidate); n++) candidate = $"{name} ({n})";
        return candidate;
    }
}
