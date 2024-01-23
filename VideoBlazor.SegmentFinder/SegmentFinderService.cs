using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;
using VideoBlazor.Domain.Enum;
using VideoBlazor.Domain.Interfaces;
using VideoBlazor.Domain.Models;
using VideoBlazor.Domain.Services;
using VideoBlazor.Shared.Utils;
using VideoPlayer.Domain.Extensions;
using VideoPlayer.Shared;

namespace VideoBlazor.SegmentFinder;

public class SegmentFinderService
{
    private string WorkDirectory = string.Empty;

    private ConcurrentQueue<Task> taskQueue = new ConcurrentQueue<Task>();
    private SemaphoreSlim semaphore;
    private int maxConcurrentTasks = 5; // Adjust as needed

    private readonly IMediaItemRepository _mediaItemRepository;
    private readonly MediaItemRefreshService _mediaItemRefreshService;
    private readonly ILogger<SegmentFinderService> _logger;

    public SegmentFinderService(IMediaItemRepository mediaItemRepository, MediaItemRefreshService mediaItemRefreshService, ILogger<SegmentFinderService> logger)
    {
        _mediaItemRefreshService = mediaItemRefreshService;
        _mediaItemRepository = mediaItemRepository;
        _logger = logger;

        WorkDirectory = Path.Combine(DirUtils.DataFolder, "segmentFinder\\");
        Directory.CreateDirectory(WorkDirectory);
        semaphore = new SemaphoreSlim(maxConcurrentTasks);
    }

    public void QueueDetectionJob(Guid itemId, MediaFile file)
    {
        var task = Task.Run(async () =>
        {
            await semaphore.WaitAsync();
            try
            {
                //job();
            }
            catch(Exception e)
            {
                //Handle any error and mark as errored
            }
            finally
            {
                semaphore.Release();
            }
        });

        taskQueue.Enqueue(task);
    }

    /*
         First find segments in two files -> scan library and mark all files as intro present
         Recheck if there's unprocessed segments - repeat the process.
            Might happen on next season changing intro or short intros

         Small percentage of files will have no intro or shortened one. Not highest priority to process these.
         If the intro is short or not regular - maybe it should actually be shown so all good and not much harm.

         Edge cases:
         
         False positives - currently need to filter out silence, now there's only minimum segment length.
         */

    public async Task AutoDetect(MediaItem item)
    {
        //TODO should be executed if no segments exist
        await AutoDetectIntro(item);
        await AutoDetectOutro(item);

        
        //TODO cleanup files
    }

    public async Task ScanLibrary(MediaItem item)
    {
        //TODO put in separate method and loop
        item = await _mediaItemRepository.LoadMediaItem(item.Id);

        await ScanLibraryIntro(item);

        _mediaItemRefreshService.RequestRefresh(item.Id);

        await ScanLibraryOutro(item);

        _mediaItemRefreshService.RequestRefresh(item.Id);
    }

    private async Task AutoDetectIntro(MediaItem item)
    {
        try
        {
            var files = LoadFilesForProcessing(item, SegmentType.Intro, 2);
            if(files is null)
            {
                return;
            }

            var (matchingSegments, mediaFile, audioFilePath) = await DetectIntroSegments(files);
            await ProcessSegments(matchingSegments, mediaFile, audioFilePath, item, SegmentType.Intro);

        }
        catch (Exception e)
        {
            _logger.LogError(e, "Counldn't autodetect intro {mediaItemId}, {title}", item.Id, item.Title);
        }
    }

    private async Task AutoDetectOutro(MediaItem item)
    {
        try
        {
            var files = LoadFilesForProcessing(item, SegmentType.Outro, 2);
            if (files is null)
            {
                return;
            }

            var (matchingSegments, mediaFile, audioFilePath) = await DetectOutroSegments(files);
            await ProcessSegments(matchingSegments, mediaFile, audioFilePath, item, SegmentType.Outro);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Counldn't autodetect outro {mediaItemId}, {title}", item.Id, item.Title);
        }
    }

    private List<MediaFile>? LoadFilesForProcessing(MediaItem item, SegmentType type, int limit = int.MaxValue)
    {
        var files = GetUnprocessedFiles(item, type);
        if (files.Count < 2)
        {
            files = GetNoSegmentFiles(item, type);
            if (files.Count < 2)
            {
                return null;
            }
        }
            
        return limit == int.MaxValue ? files : files.Take(limit).ToList();
    }

    private async Task ScanLibraryIntro(MediaItem item)
    {
        var introSession = new FingerprintSession();
        await introSession.InitializeSessionAsync(item.Segments.Where(s => s.Type == SegmentType.Intro));

        var files = LoadFilesForProcessing(item, SegmentType.Intro);
        if(files is null)
        {
            return;
        }

        foreach (var file in files)
        {
            var (audioFile, _) = AudioExtractor.ExtractAudioFile(file.Path, file.GetIntroFilename(), WorkDirectory, 0, 360);
            file.IntroTimes = await introSession.DetectSegments(audioFile);
            file.IntroProcessed = true;

            await _mediaItemRepository.SaveMediaItem(item);
            _logger.LogInformation($"Saved intro segments in file {file.Id}");
        }
    }

    private async Task ScanLibraryOutro(MediaItem item)
    {
        var outroTimeSeconds = 120;

        var introSession = new FingerprintSession();
        await introSession.InitializeSessionAsync(item.Segments.Where(s => s.Type == SegmentType.Outro));

        var files = LoadFilesForProcessing(item, SegmentType.Outro);
        if (files is null)
        {
            return;
        }

        foreach (var file in files)
        {
            var (audioFile, extractedLength) = AudioExtractor.ExtractAudioFile(file.Path, file.GetOutroFilename(), WorkDirectory, -outroTimeSeconds);
            file.OutroTimes = await introSession.DetectSegments(audioFile);

            // Convert start time to negative from the end of file
            foreach(var outroTime in file.OutroTimes)
            {
                outroTime.StartSeconds = -(extractedLength - outroTime.StartSeconds);
            }

            file.OutroProcessed = true;

            await _mediaItemRepository.SaveMediaItem(item);
            _logger.LogInformation($"Saved outro segments in file {file.Id}");
        }
    }

    /// <summary>
    /// Detecs segments from the intro files
    /// </summary>
    /// <param name="files">Media files to process</param>
    /// <returns>Segment locations, MediaFile instance and a path to audio file with these segments</returns>
    private async Task<(List<SegmentLocation>, MediaFile, string)> DetectIntroSegments(List<MediaFile> files)
    {
        /*
         TODO The very first audio file or first in new batch (when intro changes in season) might fail because of very delayed intro or no intro at all.
           - need to somehow not break detection and move on to next files.
         */
        var (audioFile1, _) = AudioExtractor.ExtractAudioFile(files[0].Path, files[0].GetIntroFilename(), WorkDirectory, 0, 360);
        var (audioFile2, _) = AudioExtractor.ExtractAudioFile(files[1].Path, files[1].GetIntroFilename(), WorkDirectory, 0, 360);

        return (await PatternFinder.FindMatchingAudio(audioFile1, audioFile2), files[1], audioFile2);
    }

    /// <summary>
    /// Detecs segments from the intro files
    /// </summary>
    /// <param name="files">Media files to process</param>
    /// <returns>Segment locations, MediaFile instance and a path to audio file with these segments</returns>
    private async Task<(List<SegmentLocation>, MediaFile, string)> DetectOutroSegments(List<MediaFile> files)
    {
        /*
         TODO The very first audio file or first in new batch (when intro changes in season) might fail because of very delayed intro or no intro at all.
           - need to somehow not break detection and move on to next files.
         */
        var (audioFile1, _) = AudioExtractor.ExtractAudioFile(files[0].Path, files[0].GetOutroFilename(), WorkDirectory, -120);
        var (audioFile2, _) = AudioExtractor.ExtractAudioFile(files[1].Path, files[1].GetOutroFilename(), WorkDirectory, -120);

        return (await PatternFinder.FindMatchingAudio(audioFile1, audioFile2), files[1], audioFile2);
    }

    private async Task ProcessSegments(List<SegmentLocation> segments, MediaFile file, string audioPath, MediaItem item, SegmentType segmentType)
    {
        List<SegmentInfo> newSegments = new();

        foreach (var segment in segments)
        {
            var segmentId = Guid.NewGuid();
            var outputPath = Path.Combine(WorkDirectory, $"{file.Id}-{segmentId}.wav");

            // Split the segment from the original audio files
            AudioExtractor.SplitWavFile(audioPath, segment.StartSeconds, segment.Length, outputPath);

            // Create SegmentInfo instances for each segment
            newSegments.Add(new SegmentInfo
            {
                Id = segmentId,
                FilePath = outputPath,
                Length = segment.Length,
                Type = segmentType
            });
        }

        await _mediaItemRepository.AddSegmentInfo(item.Id, newSegments);
        _mediaItemRefreshService.RequestRefresh(item.Id);
    }

    private static List<MediaFile> GetUnprocessedFiles(MediaItem item, SegmentType segmentType)
    {
        List<string> filesToIterate = new List<string>();

        var segmentHistory = new List<ItemHistory>();
        var fileList = new List<MediaFile>();
        var filteredFiles = item.Files
            .Where(f => f.Type == FileType.File && segmentType == SegmentType.Intro ? !f.IntroProcessed : !f.OutroProcessed)
            .ToList();
        //TODO when torrents are available - filter out the unfinished files. Now it assumes all files are processable.
        //TODO files are not sorted with getnext algorithm, ok for now

        return filteredFiles;
    }

    private static List<MediaFile> GetNoSegmentFiles(MediaItem item, SegmentType segmentType)
    {
        List<string> filesToIterate = new List<string>();

        var segmentHistory = new List<ItemHistory>();
        var fileList = new List<MediaFile>();
        var filteredFiles = item.Files
            .Where(f => f.Type == FileType.File && segmentType == SegmentType.Intro ? !f.IntroTimes.Any() : !f.OutroTimes.Any())
            .ToList();
        //TODO when torrents are available - filter out the unfinished files. Now it assumes all files are processable.
        //TODO files are not sorted with getnext algorithm, ok for now

        return filteredFiles;
    }
}
