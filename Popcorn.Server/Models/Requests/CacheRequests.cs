using System.Text.Json.Serialization;

namespace Popcorn.Server.Models.Requests;

/// <summary>What a cached file is for (Settings → Storage).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CacheKind>))]
public enum CacheKind
{
    /// <summary>Seek bar thumbnails: <c>ThumbnailsCache/</c> (web and native players).</summary>
    Thumbnails,
    /// <summary>MP4 remuxes the web player streams (<c>StreamCache/&lt;fileId&gt;-audio&lt;track&gt;.mp4</c>).</summary>
    Remux,
    /// <summary>Audio tracks converted for the web player (<c>StreamCache/&lt;fileId&gt;-&lt;track&gt;.m4a</c>).</summary>
    Audio,
    /// <summary>Extracted / converted subtitle tracks (<c>StreamCache/&lt;fileId&gt;-&lt;track&gt;.vtt|ass|...</c>).</summary>
    Subtitles,
}

/// <summary>Totals of one kind of cached data.</summary>
public class CacheKindSummary
{
    public CacheKind Kind { get; init; }
    /// <summary>Folders it is kept in.</summary>
    public required List<string> Folders { get; init; }
    public int Files { get; init; }
    public long Bytes { get; init; }
}

/// <summary>The cached files of one kind made for one media file.</summary>
public class CacheEntry
{
    public CacheKind Kind { get; init; }
    /// <summary>The media file they were made from; null when the name doesn't start with a file id.</summary>
    public Guid? FileId { get; init; }
    /// <summary>Its media item; null when the file is no longer in the library.</summary>
    public Guid? ItemId { get; init; }
    public string? ItemTitle { get; init; }
    /// <summary>Path of the media file; null when it is no longer in the library.</summary>
    public string? FilePath { get; init; }
    public int Files { get; init; }
    public long Bytes { get; init; }
    /// <summary>Latest write time of its files (UTC): when it was last made or refreshed.</summary>
    public DateTime LastWrite { get; init; }
    /// <summary>A remux still being written (<c>.part</c>).</summary>
    public bool InProgress { get; init; }
}

/// <summary><c>CacheList</c> answer: everything in the cache folders.</summary>
public class CacheOverview
{
    public required List<CacheKindSummary> Kinds { get; init; }
    /// <summary>Largest first.</summary>
    public required List<CacheEntry> Entries { get; init; }
    /// <summary>What the start-up cleanup keeps: files newer than this many days…</summary>
    public int MaxAgeDays { get; init; }
    /// <summary>…and, for the stream cache, at most this many bytes.</summary>
    public long StreamCacheLimitBytes { get; init; }
    /// <summary>The log files of all apps (kept <c>LogRetentionDays</c> in the settings).</summary>
    public required LogFolderSummary Logs { get; init; }
}

public class LogFolderSummary
{
    public required string Folder { get; init; }
    public int Files { get; init; }
    public long Bytes { get; init; }
    /// <summary>Largest single file (each is capped at <c>DailyLogFileSink.MaxFileBytes</c>).</summary>
    public long LargestFileBytes { get; init; }
    public long MaxFileBytes { get; init; }
}

/// <summary><c>CacheClear</c>: deletes cached files; <see cref="Kind"/> / <see cref="FileId"/> null mean all of them.</summary>
public class CacheClearRequest
{
    public CacheKind? Kind { get; init; }
    public Guid? FileId { get; init; }
}

/// <summary><c>CacheClear</c> answer (the new <c>CacheList</c> follows it).</summary>
public class CacheClearResult
{
    public int Files { get; init; }
    public long Bytes { get; init; }
    /// <summary>Left in place: still being written (<c>.part</c>) or could not be deleted (open on Windows).</summary>
    public int Skipped { get; init; }
}
