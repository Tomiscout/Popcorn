using SoundFingerprinting.Builder;
using SoundFingerprinting.Data;
using SoundFingerprinting.InMemory;
using SoundFingerprinting.Query;
using Popcorn.Domain.Models;

namespace Popcorn.SegmentFinder;
public class FingerprintSession
{
    private InMemoryModelService modelService;

    public FingerprintSession()
    {
        modelService = new InMemoryModelService();
    }

    public async Task InitializeSessionAsync(IEnumerable<SegmentInfo> segments)
    {
        foreach (var segment in segments)
        {
            var segmentPath = Path.Combine(SegmentFinderService.WorkDirectory, segment.FilePath);
            var hash = await PatternFinder.BuildHash(segmentPath);
            var track = new TrackInfo(segment.Id.ToString(), segmentPath, segment.FilePath);
            modelService.Insert(track, hash);
        }
    }

    public async Task<List<SegmentLocation>> DetectSegments(Guid fileId, string audioPath)
    {
        var queryResult = await QueryCommandBuilder.Instance
                                 .BuildQueryCommand()
                                 .From(audioPath)
                                 .UsingServices(modelService)
                                 .Query();

        return queryResult.ResultEntries
            .Select(entry =>
            {
                entry.Deconstruct(out ResultEntry? audio, out _);
                return audio;
            })
            .Where(entry => entry != null && entry.TrackCoverageWithPermittedGapsLength >= 2)
            .OrderByDescending(e => e!.TrackCoverageWithPermittedGapsLength)
            .Select(e => new SegmentLocation()
            {
                SegmentId = Guid.Parse(e!.Track.Id),
                FileId = fileId,
                Length = e!.TrackCoverageWithPermittedGapsLength,
                StartSeconds = e.QueryMatchStartsAt
            })
            .ToList();
    }

    public void Dispose()
    {
        modelService = null;
    }
}
