namespace Popcorn.Domain.Models;

/// <summary>Settings of the built-in torrent client and direct downloader (Settings → Downloads).</summary>
public class DownloadSettings
{
    /// <summary>Downloads go to a folder per download inside this one. Empty = <c>~/Downloads/Popcorn</c>.</summary>
    public string DownloadFolder { get; set; } = string.Empty;

    /// <summary>File links of kind Direct are downloaded by Popcorn (true) or opened externally (browser / system).</summary>
    public bool UseInternalDirectDownloader { get; set; } = true;

    /// <summary>Total download limit in KiB/s, 0 = unlimited.</summary>
    public int MaxDownloadKiBps { get; set; }

    /// <summary>Total upload limit in KiB/s, 0 = unlimited.</summary>
    public int MaxUploadKiBps { get; set; }

    /// <summary>A finished torrent stops seeding once uploaded / size reaches this. 0 = stop as soon as it is complete.</summary>
    public double SeedRatioLimit { get; set; } = 1.0;

    /// <summary>A finished torrent also stops after seeding this many minutes. 0 = no time limit (ratio only).</summary>
    public int SeedTimeLimitMinutes { get; set; }

    /// <summary>Incoming peer connections (TCP + uTP + DHT). 0 = random port chosen by the OS.</summary>
    public int ListenPort { get; set; } = 51413;

    public int MaxConnections { get; set; } = 5000;

    public string ResolveDownloadFolder() =>
        string.IsNullOrWhiteSpace(DownloadFolder)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "Popcorn")
            : DownloadFolder;
}
