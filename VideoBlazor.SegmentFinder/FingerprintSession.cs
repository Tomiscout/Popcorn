using SoundFingerprinting.Builder;
using SoundFingerprinting.Data;
using SoundFingerprinting.InMemory;
using SoundFingerprinting.Query;
using VideoBlazor.Domain.Models;

namespace VideoBlazor.SegmentFinder;
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
            var hash = await PatternFinder.BuildHash(segment.FilePath);
            var track = new TrackInfo(segment.Id.ToString(), segment.FilePath, Path.GetFileName(segment.FilePath));
            modelService.Insert(track, hash);
        }
    }

    public async Task<List<SegmentLocation>> DetectSegments(string audioPath)
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
                Length = e!.TrackCoverageWithPermittedGapsLength,
                StartSeconds = e.QueryMatchStartsAt
            })
            .ToList();
    }
}
