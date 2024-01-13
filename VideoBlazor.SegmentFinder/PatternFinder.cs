using SoundFingerprinting.Builder;
using SoundFingerprinting.Data;
using SoundFingerprinting.InMemory;
using SoundFingerprinting.Query;
using SoundFingerprinting.Strides;
using VideoBlazor.Domain.Models;

namespace VideoBlazor.SegmentFinder;
public static class PatternFinder
{
    public static async Task<List<SegmentLocation>> FindMatchingAudio(string firstFile, string secondFile)
    {
        //Fingerprint first track
        var hash1 = await BuildHash(firstFile);

        var modelService = new InMemoryModelService();
        var track = new TrackInfo("match", string.Empty, string.Empty);
        modelService.Insert(track, hash1);

        //Query with second track
        var queryResult = await QueryCommandBuilder.Instance
                                 .BuildQueryCommand()
                                 .From(secondFile)
                                 .UsingServices(modelService)
                                 .Query();

        //Format results
        var audioResults = queryResult.ResultEntries
        .Select(entry =>
        {
            entry.Deconstruct(out ResultEntry? audio, out _);
            return audio;
        })
        .Where(entry => entry != null && entry.TrackCoverageWithPermittedGapsLength >= 2)
        .OrderByDescending(e => e!.TrackCoverageWithPermittedGapsLength)
        .ToList();

        return audioResults.Select(e => new SegmentLocation()
        {
            SegmentId = Guid.NewGuid(),
            Length = e!.TrackCoverageWithPermittedGapsLength,
            StartSeconds = e.QueryMatchStartsAt
        }).ToList();
    }

    public static async Task<AVHashes> BuildHash(string path)
    {
        return await FingerprintCommandBuilder.Instance
                            .BuildFingerprintCommand()
                            .From(path)
                            .WithFingerprintConfig(config =>
                            {
                                // set stride between consecutive fingerprints to 1_024 samples
                                // https://github.com/AddictedCS/soundfingerprinting/wiki/Algorithm-Configuration
                                config.Audio.Stride = new IncrementalStaticStride(512); //default
                                return config;
                            })
                            .Hash();
    }
}
