using SoundFingerprinting.Builder;
using SoundFingerprinting.Data;
using SoundFingerprinting.InMemory;
using SoundFingerprinting.Query;

namespace Popcorn.SegmentFinder;

/// <summary>Audio match of a reference track inside a queried file. Times in seconds.</summary>
/// <param name="TrackId">Id the reference was added with.</param>
/// <param name="QueryStart">Where the match starts in the queried audio.</param>
/// <param name="TrackStart">Where the match starts in the reference.</param>
/// <param name="Length">Matched length (coverage of the reference, with permitted gaps).</param>
public record AudioMatch(string TrackId, double QueryStart, double TrackStart, double Length)
{
    public double QueryEnd => QueryStart + Length;

    /// <summary>Query time of the reference's start (reference time + offset = query time).</summary>
    public double Offset => QueryStart - TrackStart;
}

/// <summary>Set of reference audio tracks that query files are matched against (SoundFingerprinting, in memory).</summary>
internal class Fingerprinter
{
    /// <summary>How far from the fingerprints' guess the envelope alignment looks (seen off by ~3 s).</summary>
    private const double AlignSearchSeconds = 5;

    private readonly InMemoryModelService _model = new();
    private readonly Dictionary<string, AudioEnvelope> _envelopes = new();

    public bool IsEmpty { get; private set; } = true;

    /// <summary>Loudness of a reference (kept, its wav may be a temporary file).</summary>
    public AudioEnvelope Envelope(string trackId) => _envelopes[trackId];

    public async Task AddAsync(string trackId, string wavPath)
    {
        _envelopes[trackId] = AudioEnvelope.Read(wavPath);
        var hashes = await FingerprintCommandBuilder.Instance
            .BuildFingerprintCommand()
            .From(wavPath)
            .Hash();
        _model.Insert(new TrackInfo(trackId, trackId, trackId), hashes);
        IsEmpty = false;
    }

    /// <summary>
    /// All matches of the references in <paramref name="wavPath"/>, a reference can match several times. Match
    /// times are moved to where the loudness of the matched part lines up (<see cref="AudioEnvelope.Align"/>).
    /// </summary>
    public async Task<List<AudioMatch>> QueryAsync(string wavPath, AudioEnvelope envelope)
    {
        var result = await QueryCommandBuilder.Instance
            .BuildQueryCommand()
            .From(wavPath)
            .UsingServices(_model)
            .Query();

        return result.ResultEntries
            .Select(e => e.Audio)
            .OfType<ResultEntry>()
            .Select(a =>
            {
                var match = new AudioMatch(a.Track.Id, a.QueryMatchStartsAt, a.TrackMatchStartsAt, a.TrackCoverageWithPermittedGapsLength);
                var offset = envelope.Align(_envelopes[match.TrackId], match.TrackStart, match.TrackStart + match.Length,
                    match.Offset, AlignSearchSeconds);
                return match with { QueryStart = match.TrackStart + offset };
            })
            .ToList();
    }

    /// <summary>
    /// Merges matches that overlap or are separated by at most <paramref name="maxGap"/> seconds (a song with
    /// dialogue over it comes back in pieces). The merged match keeps the id of its longest part.
    /// </summary>
    public static List<AudioMatch> Merge(IEnumerable<AudioMatch> matches, double maxGap)
    {
        var merged = new List<(AudioMatch Longest, double Start, double End)>();
        foreach (var m in matches.OrderBy(m => m.QueryStart))
        {
            if (merged.Count > 0 && m.QueryStart <= merged[^1].End + maxGap)
            {
                var last = merged[^1];
                merged[^1] = (m.Length > last.Longest.Length ? m : last.Longest, last.Start, Math.Max(last.End, m.QueryEnd));
            }
            else
            {
                merged.Add((m, m.QueryStart, m.QueryEnd));
            }
        }
        return merged
            .Select(x => x.Longest with { QueryStart = x.Start, TrackStart = x.Start - x.Longest.Offset, Length = x.End - x.Start })
            .ToList();
    }
}
