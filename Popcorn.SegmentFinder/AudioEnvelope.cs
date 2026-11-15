using System.Buffers.Binary;
using System.Text;

namespace Popcorn.SegmentFinder;

/// <summary>
/// Loudness of a fingerprint wav (16-bit PCM mono, <see cref="AudioExtractor"/>) in ~10 ms frames. Fills the two
/// gaps of the fingerprints:
/// <list type="bullet">
/// <item>Silence has no fingerprints, so a segment with silence in it (an outro of credits, silence, a logo sting,
/// silence) only matches in pieces, shorter than it is. Silent frames tell where a match may grow.</item>
/// <item>Fingerprint match times are off by up to a few seconds; correlating the envelopes finds the exact offset.</item>
/// </list>
/// </summary>
internal sealed class AudioEnvelope
{
    /// <summary>Frames quieter than this (RMS, dBFS) are silence. Digital silence and the noise floor of a fade.</summary>
    private const float SilenceDb = -50;
    /// <summary>Envelope floor: below it all is the same silence (keeps the correlation off the noise).</summary>
    private const float FloorDb = -60;
    private const double TargetFrameSeconds = 0.01;
    /// <summary>Offsets are only moved when the envelopes clearly agree there.</summary>
    private const double MinCorrelation = 0.6;

    private readonly float[] _db;

    /// <summary>Exact frame length (whole samples per frame).</summary>
    public double FrameSeconds { get; }

    public double Duration => _db.Length * FrameSeconds;

    private AudioEnvelope(float[] db, double frameSeconds)
    {
        _db = db;
        FrameSeconds = frameSeconds;
    }

    public static AudioEnvelope Read(string wavPath)
    {
        var bytes = File.ReadAllBytes(wavPath);
        if (bytes.Length < 12 || Encoding.ASCII.GetString(bytes, 0, 4) != "RIFF" || Encoding.ASCII.GetString(bytes, 8, 4) != "WAVE")
            throw new InvalidDataException($"Not a wav file: {wavPath}");

        int sampleRate = 0, channels = 0, bits = 0, dataStart = -1, dataLength = 0;
        var pos = 12;
        while (pos + 8 <= bytes.Length)
        {
            var id = Encoding.ASCII.GetString(bytes, pos, 4);
            var length = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(pos + 4));
            var body = pos + 8;
            if (id == "fmt " && body + 16 <= bytes.Length)
            {
                channels = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(body + 2));
                sampleRate = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(body + 4));
                bits = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(body + 14));
            }
            else if (id == "data")
            {
                dataStart = body;
                // Streamed wavs (ffmpeg to a pipe) leave the length unset; the data then runs to the end.
                dataLength = (int)Math.Min(length, (uint)(bytes.Length - body));
                break;
            }
            pos = body + (int)Math.Min(length + (length & 1), (uint)(bytes.Length - body));
        }
        if (dataStart < 0 || sampleRate <= 0 || channels != 1 || bits != 16)
            throw new InvalidDataException($"Expected a 16-bit mono wav: {wavPath}");

        var perFrame = Math.Max(1, (int)Math.Round(sampleRate * TargetFrameSeconds));
        var samples = bytes.AsSpan(dataStart, dataLength);
        var db = new float[dataLength / 2 / perFrame];
        for (var f = 0; f < db.Length; f++)
        {
            double sum = 0;
            for (var i = 0; i < perFrame; i++)
            {
                double v = BinaryPrimitives.ReadInt16LittleEndian(samples[((f * perFrame + i) * 2)..]) / 32768.0;
                sum += v * v;
            }
            db[f] = (float)Math.Max(FloorDb, 10 * Math.Log10(sum / perFrame + 1e-12));
        }
        return new AudioEnvelope(db, (double)perFrame / sampleRate);
    }

    private int Frame(double seconds) => (int)Math.Round(seconds / FrameSeconds);

    private bool SilentFrame(int frame) => frame >= 0 && frame < _db.Length && _db[frame] <= SilenceDb;

    public bool IsSilent(double seconds) => SilentFrame(Frame(seconds));

    /// <summary>Start of the silence that ends at <paramref name="seconds"/> (itself when the audio there is not silent).</summary>
    public double SilenceStartBefore(double seconds)
    {
        var frame = Frame(seconds);
        while (SilentFrame(frame - 1)) frame--;
        return Math.Min(seconds, frame * FrameSeconds);
    }

    /// <summary>End of the silence that starts at <paramref name="seconds"/> (itself when the audio there is not silent).</summary>
    public double SilenceEndAfter(double seconds)
    {
        var frame = Frame(seconds);
        while (SilentFrame(frame)) frame++;
        return Math.Max(seconds, frame * FrameSeconds);
    }

    /// <summary>
    /// Where <paramref name="from"/>..<paramref name="to"/> of <paramref name="other"/> lines up in this audio, as
    /// the offset to add to <paramref name="other"/>'s times: the best envelope correlation within
    /// <paramref name="search"/> seconds of <paramref name="guess"/>. The guess when nothing correlates clearly
    /// (the part is flat, or differs, e.g. dialogue over the song).
    /// </summary>
    public double Align(AudioEnvelope other, double from, double to, double guess, double search)
    {
        var first = Math.Max(0, other.Frame(from));
        var last = Math.Min(other._db.Length, other.Frame(to));
        if (last - first < 10 || Math.Abs(other.FrameSeconds - FrameSeconds) > 1e-9) return guess;

        var reference = other._db.AsSpan(first, last - first);
        var guessLag = Frame(guess + from);
        var range = Frame(search);
        var best = MinCorrelation;
        var bestLag = (int?)null;
        for (var lag = guessLag - range; lag <= guessLag + range; lag++)
        {
            // Only offsets where the whole part lies inside this audio.
            if (lag < 0 || lag + reference.Length > _db.Length) continue;
            var correlation = Correlation(reference, _db.AsSpan(lag, reference.Length));
            if (correlation > best)
            {
                best = correlation;
                bestLag = lag;
            }
        }
        return bestLag is { } l ? l * FrameSeconds - first * other.FrameSeconds : guess;
    }

    private static double Correlation(ReadOnlySpan<float> x, ReadOnlySpan<float> y)
    {
        double sx = 0, sy = 0, sxx = 0, syy = 0, sxy = 0;
        for (var i = 0; i < x.Length; i++)
        {
            double a = x[i], b = y[i];
            sx += a;
            sy += b;
            sxx += a * a;
            syy += b * b;
            sxy += a * b;
        }
        var n = x.Length;
        var varX = n * sxx - sx * sx;
        var varY = n * syy - sy * sy;
        // A flat part (all silence, a steady tone) has no shape to line up.
        if (varX <= 1e-6 * n * n || varY <= 1e-6 * n * n) return 0;
        return (n * sxy - sx * sy) / Math.Sqrt(varX * varY);
    }

    /// <summary>
    /// Seconds of <paramref name="from"/>..<paramref name="to"/> (times of <paramref name="other"/>) that are silent
    /// in both audios, <paramref name="other"/> shifted by <paramref name="offset"/>.
    /// </summary>
    public double SharedSilence(AudioEnvelope other, double offset, double from, double to)
    {
        var silent = 0;
        for (var f = Math.Max(0, other.Frame(from)); f < Math.Min(other._db.Length, other.Frame(to)); f++)
        {
            if (other.SilentFrame(f) && IsSilent(f * other.FrameSeconds + offset)) silent++;
        }
        return silent * other.FrameSeconds;
    }

    /// <summary>
    /// Grows <paramref name="start"/>..<paramref name="end"/> (times of this audio) over the silence both audios share
    /// next to it, <paramref name="other"/> shifted by <paramref name="offset"/>.
    /// </summary>
    public (double Start, double End) ExtendOverSharedSilence(AudioEnvelope other, double offset, double start, double end)
    {
        while (IsSilent(start - FrameSeconds) && other.IsSilent(start - FrameSeconds - offset)) start -= FrameSeconds;
        while (IsSilent(end) && other.IsSilent(end - offset)) end += FrameSeconds;
        return (Math.Max(0, start), Math.Min(Duration, end));
    }
}
