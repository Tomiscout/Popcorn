using System.Globalization;
using System.Text.Json.Serialization;
using FFMpegCore;

namespace Popcorn.Shared.Ffmpeg;

public class ChapterInfo
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("startSeconds")]
    public double StartSeconds { get; set; }

    [JsonPropertyName("endSeconds")]
    public double EndSeconds { get; set; }
}

/// <summary>Technical details of a file as labelled rows per section; the player only displays them.</summary>
public class MediaInfo
{
    [JsonPropertyName("sections")]
    public List<MediaInfoSection> Sections { get; set; } = [];
}

public class MediaInfoSection
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("rows")]
    public List<MediaInfoRow> Rows { get; set; } = [];
}

public record MediaInfoRow(
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("value")] string Value);

/// <summary>Turns an ffprobe analysis into what the players' Media info tab and segment label show.</summary>
public static class MediaInfoBuilder
{
    public static List<ChapterInfo> Chapters(IMediaAnalysis probe) => probe.Chapters
        .Where(c => !string.IsNullOrWhiteSpace(c.Title) && c.End > c.Start)
        .OrderBy(c => c.Start)
        .Select(c => new ChapterInfo { Title = c.Title, StartSeconds = c.Start.TotalSeconds, EndSeconds = c.End.TotalSeconds })
        .ToList();

    /// <param name="path">The file's path (a download still in progress may be probed from a stream and not exist yet).</param>
    public static MediaInfo Build(IMediaAnalysis probe, string path)
    {
        var sections = new List<MediaInfoSection> { File(probe, path) };

        sections.AddRange(probe.VideoStreams.Select((s, i) => Section(Numbered("Video", i, probe.VideoStreams.Count),
            ("Codec", Codec(s)),
            ("Profile", s.Profile),
            ("Resolution", $"{s.Width} × {s.Height}"),
            ("Aspect ratio", s.DisplayAspectRatio is var (w, h) && w > 0 && h > 0 ? $"{w}:{h}" : null),
            ("Frame rate", s.FrameRate > 0 ? $"{s.FrameRate.ToString("0.###", CultureInfo.InvariantCulture)} fps" : null),
            ("Bit rate", BitRate(s.BitRate)),
            ("Pixel format", s.PixelFormat),
            ("Bit depth", s.BitsPerRawSample > 0 ? $"{s.BitsPerRawSample} bit" : null),
            ("Color space", s.ColorSpace),
            ("Color range", s.ColorRange),
            ("Color primaries", s.ColorPrimaries),
            ("Color transfer", s.ColorTransfer),
            ("Language", s.Language),
            ("Title", Tag(s, "title")))));

        sections.AddRange(probe.AudioStreams.Select((s, i) => Section(Numbered("Audio", i, probe.AudioStreams.Count),
            ("Codec", Codec(s)),
            ("Profile", s.Profile),
            ("Channels", string.IsNullOrEmpty(s.ChannelLayout) ? s.Channels.ToString() : $"{s.Channels} ({s.ChannelLayout})"),
            ("Sample rate", s.SampleRateHz > 0 ? $"{s.SampleRateHz} Hz" : null),
            ("Bit rate", BitRate(s.BitRate)),
            ("Language", s.Language),
            ("Title", Tag(s, "title")))));

        sections.AddRange(probe.SubtitleStreams.Select((s, i) => Section(Numbered("Subtitles", i, probe.SubtitleStreams.Count),
            ("Codec", Codec(s)),
            ("Language", s.Language),
            ("Title", Tag(s, "title")))));

        return new MediaInfo { Sections = sections };
    }

    private static MediaInfoSection File(IMediaAnalysis probe, string path)
    {
        var format = probe.Format;
        var file = new FileInfo(path);
        return Section("File",
            ("Name", Path.GetFileName(path)),
            ("Folder", Path.GetDirectoryName(path)),
            ("Size", file.Exists ? Size(file.Length) : null),
            ("Container", string.IsNullOrEmpty(format.FormatLongName) ? format.FormatName : format.FormatLongName),
            ("Duration", format.Duration > TimeSpan.Zero ? format.Duration.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture) : null),
            ("Bit rate", BitRate((long)format.BitRate)),
            ("Chapters", probe.Chapters.Count > 0 ? probe.Chapters.Count.ToString() : null));
    }

    private static MediaInfoSection Section(string title, params (string Label, string? Value)[] rows) => new()
    {
        Title = title,
        Rows = rows.Where(r => !string.IsNullOrWhiteSpace(r.Value)).Select(r => new MediaInfoRow(r.Label, r.Value!)).ToList(),
    };

    private static string Numbered(string title, int index, int count) => count > 1 ? $"{title} {index + 1}" : title;

    private static string Codec(MediaStream s) =>
        string.IsNullOrEmpty(s.CodecLongName) ? s.CodecName : $"{s.CodecName} ({s.CodecLongName})";

    private static string? Tag(MediaStream s, string name) => s.Tags?.GetValueOrDefault(name);

    private static string? BitRate(long bitsPerSecond) => bitsPerSecond switch
    {
        <= 0 => null,
        < 1_000_000 => $"{bitsPerSecond / 1000} kb/s",
        _ => (bitsPerSecond / 1_000_000d).ToString("0.## 'Mb/s'", CultureInfo.InvariantCulture),
    };

    private static string Size(long bytes) => bytes >= 1L << 30
        ? (bytes / (double)(1L << 30)).ToString("0.## 'GiB'", CultureInfo.InvariantCulture)
        : (bytes / (double)(1L << 20)).ToString("0.# 'MiB'", CultureInfo.InvariantCulture);
}
