using System.Globalization;
using FFMpegCore;

namespace Popcorn.SegmentFinder;

/// <summary>Part of a file that is searched: intros live at the start, outros at the end.</summary>
public enum AudioRegion
{
    Head,
    Tail
}

/// <param name="Path">Fingerprint wav of the region.</param>
/// <param name="Start">Where the region starts in the media file, seconds.</param>
/// <param name="Length">Length of the region, seconds.</param>
public record ExtractedAudio(string Path, double Start, double Length);

/// <summary>Chapter marker of a media file, seconds.</summary>
public record Chapter(string Title, double Start, double End)
{
    public double Length => End - Start;
}

/// <param name="AudioStream">Index of the audio stream segments are searched in, null when the file has none.</param>
public record MediaProbe(int? AudioStream, double Duration, List<Chapter> Chapters);

internal static class AudioExtractor
{
    // Long enough for a cold open + intro / credits + preview, small enough to fingerprint in about a second.
    private const double HeadSeconds = 360;
    private const double TailSeconds = 300;

    // SoundFingerprinting analyses 5512 Hz mono (it resamples anything else itself, and rejects >2 channels), so
    // extracting at that rate loses nothing for matching. Listening quality is handled by the preview clips.
    private const string FingerprintFormat = "-ac 1 -ar 5512 -c:a pcm_s16le";
    private const string PreviewFormat = "-ac 2 -c:a libopus -b:a 128k";

    /// <summary>
    /// Extracts the head or tail of the file's default audio track for fingerprinting. Short files are split in
    /// half so the regions never overlap (the same audio would otherwise be both an intro and an outro).
    /// </summary>
    public static async Task<ExtractedAudio> ExtractRegionAsync(string file, MediaProbe probe, AudioRegion region, string outputPath)
    {
        var length = Math.Min(region == AudioRegion.Head ? HeadSeconds : TailSeconds, probe.Duration / 2);
        var start = region == AudioRegion.Head ? 0 : probe.Duration - length;

        await ExtractAsync(file, AudioStream(file, probe), start, length, FingerprintFormat, outputPath);
        return new ExtractedAudio(outputPath, start, length);
    }

    /// <summary>Fingerprint clip of a segment (a reference used when scanning other files).</summary>
    public static Task ExtractFingerprintAsync(string file, MediaProbe probe, double start, double length, string outputPath)
        => ExtractAsync(file, AudioStream(file, probe), start, length, FingerprintFormat, outputPath);

    /// <summary>Full quality clip of the media file for listening to a segment (Opus in Ogg).</summary>
    public static Task ExtractPreviewAsync(string file, MediaProbe probe, double start, double length, string outputPath)
        => ExtractAsync(file, AudioStream(file, probe), start, length, PreviewFormat, outputPath);

    private static int AudioStream(string file, MediaProbe probe) =>
        probe.AudioStream ?? throw new InvalidOperationException($"No audio track in {file}");

    /// <summary>Audio stream (the default one, else the primary one), duration and chapters of a media file.</summary>
    public static async Task<MediaProbe> ProbeAsync(string file)
    {
        var probe = await FFProbe.AnalyseAsync(file);
        var stream = probe.AudioStreams.FirstOrDefault(s => s.Disposition?.GetValueOrDefault("default") == true)
                     ?? probe.PrimaryAudioStream;

        var duration = probe.Duration.TotalSeconds;
        if (duration <= 0) throw new InvalidOperationException($"Unknown duration of {file}");

        var chapters = probe.Chapters
            .Select(c => new Chapter(c.Title ?? "", c.Start.TotalSeconds, Math.Min(c.End.TotalSeconds, duration)))
            .Where(c => c.Length > 0)
            .OrderBy(c => c.Start)
            .ToList();
        return new MediaProbe(stream?.Index, duration, chapters);
    }

    private static async Task ExtractAsync(string file, int streamIndex, double start, double length, string format, string outputPath)
    {
        await FFMpegArguments
            .FromFileInput(file, true, o => o.Seek(TimeSpan.FromSeconds(start)))
            .OutputToFile(outputPath, true, o => o
                .WithCustomArgument($"-map 0:{streamIndex} -vn -sn -dn {format}")
                .WithCustomArgument($"-t {Format(length)}"))
            .ProcessAsynchronously();

        if (!File.Exists(outputPath)) throw new InvalidOperationException($"Failed to extract audio from {file}");
    }

    private static string Format(double seconds) => seconds.ToString("F3", CultureInfo.InvariantCulture);
}
