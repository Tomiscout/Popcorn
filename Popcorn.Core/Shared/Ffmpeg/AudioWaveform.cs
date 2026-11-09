using FFMpegCore;
using FFMpegCore.Pipes;

namespace Popcorn.Shared.Ffmpeg;

/// <summary>
/// Waveform peaks of a short window of an audio track, for the players' subtitle syncer: only the part around the
/// playback position is decoded (input seek), never the whole file.
/// </summary>
public static class AudioWaveform
{
    /// <summary>Peaks per second of audio (one per 10 ms).</summary>
    public const int PeaksPerSecond = 100;

    /// <summary>Longest window one request may decode.</summary>
    public const double MaxSeconds = 60;

    private const int SampleRate = 8000;
    private const int SamplesPerPeak = SampleRate / PeaksPerSecond;

    /// <summary>
    /// One byte per 1/<see cref="PeaksPerSecond"/> s from <paramref name="start"/>: the loudest sample of the slice,
    /// square-root scaled to 0–255 (quiet speech stays visible). Shorter than asked at the end of the file.
    /// </summary>
    /// <param name="input">File path or URL (a download stream).</param>
    /// <param name="map">ffmpeg stream specifier of the track: <c>"3"</c> (stream index), <c>"a:0"</c>.</param>
    public static async Task<byte[]> ReadPeaksAsync(string input, string map, double start, double duration,
        CancellationToken cancellationToken = default)
    {
        start = Math.Max(0, start);
        duration = Math.Clamp(duration, 0.1, MaxSeconds);

        void Input(FFMpegArgumentOptions opts) => opts.Seek(TimeSpan.FromSeconds(start));
        var arguments = Uri.TryCreate(input, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
            ? FFMpegArguments.FromUrlInput(uri, Input)
            : FFMpegArguments.FromFileInput(input, true, Input);

        using var pcm = new MemoryStream();
        await arguments
            .OutputToPipe(new StreamPipeSink(pcm), opts => opts
                .WithCustomArgument($"-map 0:{map} -vn -sn -dn")
                .WithCustomArgument(FormattableString.Invariant($"-t {duration:0.###}"))
                // Mono, speech band: dialogue stands out from music and effects, which is what syncing is about.
                .WithCustomArgument($"-ac 1 -ar {SampleRate} -af highpass=f=150,lowpass=f=4000")
                .WithCustomArgument("-c:a pcm_s16le")
                .ForceFormat("s16le"))
            .CancellableThrough(cancellationToken)
            .ProcessAsynchronously();

        return ToPeaks(pcm.GetBuffer().AsSpan(0, (int)pcm.Length));
    }

    private static byte[] ToPeaks(ReadOnlySpan<byte> pcm)
    {
        var samples = pcm.Length / 2;
        var peaks = new byte[(samples + SamplesPerPeak - 1) / SamplesPerPeak];
        for (var p = 0; p < peaks.Length; p++)
        {
            var max = 0;
            var end = Math.Min(samples, (p + 1) * SamplesPerPeak);
            for (var s = p * SamplesPerPeak; s < end; s++)
            {
                var value = Math.Abs((int)(short)(pcm[s * 2] | (pcm[s * 2 + 1] << 8)));
                if (value > max) max = value;
            }
            peaks[p] = (byte)Math.Round(Math.Sqrt(Math.Min(1.0, max / 32768.0)) * 255);
        }
        return peaks;
    }
}
