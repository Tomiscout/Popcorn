using SoundFingerprinting.Builder;
using SoundFingerprinting.Data;
using SoundFingerprinting.InMemory;
using SoundFingerprinting.Query;

namespace Popcorn.SegmentFinder;

/// <summary>Audio match of a reference track inside a queried file. Times in seconds.</summary>
/// <param name="TrackId">Id the reference was added with.</param>
/// <param name="QueryStart">Where the match starts in the queried audio.</param>
/// <param name="Length">Matched length (coverage of the reference, with permitted gaps).</param>
public record AudioMatch(string TrackId, double QueryStart, double Length)
{
    public double QueryEnd => QueryStart + Length;
}

/// <summary>Set of reference audio tracks that query files are matched against (SoundFingerprinting, in memory).</summary>
internal class Fingerprinter
{
    private readonly InMemoryModelService _model = new();

    public bool IsEmpty { get; private set; } = true;

    public async Task AddAsync(string trackId, string wavPath)
    {
        var hashes = await FingerprintCommandBuilder.Instance
            .BuildFingerprintCommand()
            .From(wavPath)
            .Hash();
        _model.Insert(new TrackInfo(trackId, trackId, trackId), hashes);
        IsEmpty = false;
    }

    /// <summary>All matches of the references in <paramref name="wavPath"/>, a reference can match several times.</summary>
    public async Task<List<AudioMatch>> QueryAsync(string wavPath)
    {
        var result = await QueryCommandBuilder.Instance
            .BuildQueryCommand()
            .From(wavPath)
            .UsingServices(_model)
            .Query();

        return result.ResultEntries
            .Select(e => e.Audio)
            .OfType<ResultEntry>()
            .Select(a => new AudioMatch(a.Track.Id, a.QueryMatchStartsAt, a.TrackCoverageWithPermittedGapsLength))
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
        return merged.Select(x => x.Longest with { QueryStart = x.Start, Length = x.End - x.Start }).ToList();
    }
}
