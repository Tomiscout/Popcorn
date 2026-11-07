using System.Text.Json;
using Popcorn.Shared.Utils;

namespace Popcorn.Downloads;

internal static class DownloadPaths
{
    public static readonly string Folder = Path.Combine(DirectoryUtils.DataFolder, "downloads");
    /// <summary>Copies of added .torrent files.</summary>
    public static readonly string Torrents = Path.Combine(Folder, "torrents");
    /// <summary>MonoTorrent cache: fast resume data, magnet metadata, DHT nodes.</summary>
    public static readonly string EngineCache = Path.Combine(Folder, "engine");
    public static readonly string RecordsFile = Path.Combine(Folder, "downloads.json");

    private static readonly char[] Invalid = Path.GetInvalidFileNameChars().Concat(['/', '\\', ':', '*', '?', '"', '<', '>', '|']).Distinct().ToArray();

    public static string SanitizeFileName(string name)
    {
        var clean = new string(name.Select(c => Invalid.Contains(c) || char.IsControl(c) ? '_' : c).ToArray()).Trim(' ', '.');
        return clean.Length > 180 ? clean[..180] : clean;
    }

    /// <summary>A folder for a new download inside <paramref name="root"/> that no other download uses.</summary>
    public static string NewDownloadFolder(string root, string name, IEnumerable<string> taken)
    {
        var baseName = SanitizeFileName(name);
        if (baseName.Length == 0) baseName = "download";
        var used = taken.ToHashSet(StringComparer.Ordinal);
        var path = Path.Combine(root, baseName);
        for (var i = 2; used.Contains(path) || Directory.Exists(path) || File.Exists(path); i++)
            path = Path.Combine(root, $"{baseName} ({i})");
        return path;
    }
}

/// <summary>Saves the download list as JSON.</summary>
internal sealed class DownloadStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private readonly SemaphoreSlim _lock = new(1, 1);

    public List<DownloadRecord> Load()
    {
        Directory.CreateDirectory(DownloadPaths.Folder);
        Directory.CreateDirectory(DownloadPaths.Torrents);
        Directory.CreateDirectory(DownloadPaths.EngineCache);
        if (!File.Exists(DownloadPaths.RecordsFile)) return [];
        using var stream = File.OpenRead(DownloadPaths.RecordsFile);
        var records = JsonSerializer.Deserialize<List<DownloadRecord>>(stream, Options) ?? [];
        foreach (var record in records.Where(r => r.SkippedFiles is not null))
        {
            foreach (var index in record.SkippedFiles!) record.FilePriorities[index] = FilePriority.Skip;
            record.SkippedFiles = null;
        }
        return records;
    }

    public async Task SaveAsync(IEnumerable<DownloadRecord> records)
    {
        await _lock.WaitAsync();
        try
        {
            var temp = DownloadPaths.RecordsFile + ".tmp";
            await using (var stream = File.Create(temp))
                await JsonSerializer.SerializeAsync(stream, records.ToList(), Options);
            File.Move(temp, DownloadPaths.RecordsFile, overwrite: true);
        }
        finally
        {
            _lock.Release();
        }
    }
}
