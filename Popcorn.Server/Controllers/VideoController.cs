using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Popcorn.Application.Extensions;
using Popcorn.Application.Services;
using Popcorn.Domain.Interfaces;
using Popcorn.Domain.Models;
using Popcorn.Downloads;
using Popcorn.Domain.Utils;
using Popcorn.Server.Controllers.Models;
using Popcorn.Server.Services;
using Popcorn.Shared.Ffmpeg;

namespace Popcorn.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class VideoController(
    IMediaFileRepository mediaFileRepository,
    FileStreamService fileStreamService,
    ItemHistoryService itemHistoryService,
    SegmentSkipService segmentSkipService,
    IMediaItemRepository mediaItemRepository,
    DownloadManager downloads,
    AudioTrackService audioTracks,
    ILogger<VideoController> logger) : ControllerBase
{
    /// <summary>Still downloading: played as is from the download stream (no remux, it needs the whole file).</summary>
    private async Task<DownloadPlaybackDto?> InProgressDownload(MediaFile mediaFile) =>
        await downloads.GetPlaybackAsync(mediaFile.Path) is { Complete: false } playback ? playback : null;

    /// <summary>Server-side URL of a download stream, for ffprobe.</summary>
    private string LocalUrl(DownloadPlaybackDto playback) =>
        $"http://127.0.0.1:{HttpContext.Connection.LocalPort}/{playback.StreamPath}";

    [HttpGet("{fileId}")]
    public async Task<IActionResult> Get(Guid fileId)
    {
        if(fileId == Guid.Empty) return NotFound();
        
        var mediaFile = await mediaFileRepository.GetFileById(fileId);
        if(mediaFile == null) return NotFound();

        if (await InProgressDownload(mediaFile) is { } playback)
        {
            await itemHistoryService.ItemWasPlayedAsync(fileId);
            return LocalRedirect("/" + playback.StreamPath);
        }
        
        if (!System.IO.File.Exists(mediaFile.Path))
            return NotFound();
        
        var streamFile = await fileStreamService.PrepareStreamFileAsync(mediaFile);
        if (streamFile != null)
        {
            await itemHistoryService.ItemWasPlayedAsync(fileId);
            return PhysicalFile(streamFile.filePath, streamFile.mimeType, enableRangeProcessing: true);
        }

        return StatusCode(500);
        //await fileStreamService.PrepareTsStreamAsync();
    }
    
    [HttpGet("{fileId}/metadata")]
    public async Task<ActionResult<MediaFileMetadata>> GetMetadata(Guid fileId)
    {
        if(fileId == Guid.Empty) return NotFound();
        
        var mediaFile = await mediaFileRepository.GetFileById(fileId);
        if(mediaFile == null) return NotFound();

        var playback = await InProgressDownload(mediaFile);
        if (playback is null && !System.IO.File.Exists(mediaFile.Path))
            return NotFound();

        // Files are tagged when scanned; this catches rows stored before that.
        if (!mediaFile.TagsProcessed)
        {
            mediaFile.ParseTags();
            await mediaFileRepository.UpdateMediaFile(mediaFile);
        }

        // ffprobe reads the stream, which waits for (and so fetches first) the parts it needs.
        var metadata = await fileStreamService.GetMediaFileMetadataAsync(mediaFile, playback is null ? null : LocalUrl(playback));
        if (metadata == null) return NotFound();

        // Not part of the cached metadata: segments change when the library is scanned.
        metadata.Segments = await segmentSkipService.GetAsync(mediaFile);
        var item = mediaFile.ItemId == Guid.Empty ? null : await mediaItemRepository.GetMediaItemWithFilesAndHistory(mediaFile.ItemId);
        metadata.ShortTitle = mediaFile.GetPlayerTitle(item);
        metadata.ResumePosition = mediaFile.GetResumePosition();
        metadata.NextFile = item?.GetNextFilePlan(mediaFile.Id);
        return Ok(metadata);
    }
    
    private sealed record AudioSource(MediaFile File, AudioTrackDto Track, string? InputUrl);

    /// <summary>The file + track behind an audio request; <c>InputUrl</c> = download stream of a file still downloading.</summary>
    private async Task<AudioSource?> FindAudioTrack(Guid fileId, int trackId)
    {
        if (fileId == Guid.Empty || trackId < 0) return null;
        var mediaFile = await mediaFileRepository.GetFileById(fileId);
        if (mediaFile == null) return null;

        var playback = await InProgressDownload(mediaFile);
        if (playback is null && !System.IO.File.Exists(mediaFile.Path)) return null;
        var inputUrl = playback is null ? null : LocalUrl(playback);

        var metadata = await fileStreamService.GetMediaFileMetadataAsync(mediaFile, inputUrl);
        var track = metadata?.AudioTracks.FirstOrDefault(t => t.Index == trackId);
        return track == null ? null : new AudioSource(mediaFile, track, inputUrl);
    }

    /// <summary>
    /// Audio track for the web player when the video has none it can play: whether the converted track is ready; else
    /// starts its conversion (kept alive by asking again). Files still downloading are not converted (it would read
    /// the whole download), they only have the live stream.
    /// </summary>
    [HttpGet("{fileId}/audio/{trackId}/prepare")]
    public async Task<ActionResult<AudioTrackState>> PrepareAudioTrack(Guid fileId, int trackId)
    {
        if (await FindAudioTrack(fileId, trackId) is not { } source) return NotFound();
        if (source.InputUrl is not null) return new AudioTrackState(Ready: false, Converting: false);
        return audioTracks.Prepare(source.File, source.Track);
    }

    /// <summary>The converted track (seekable, ranges), 404 until <see cref="PrepareAudioTrack"/> says it is ready.</summary>
    [HttpGet("{fileId}/audio/{trackId}")]
    public IActionResult GetAudioTrack(Guid fileId, int trackId) =>
        fileId != Guid.Empty && trackId >= 0 && AudioTrackService.ConvertedPath(fileId, trackId) is { } path
            ? PhysicalFile(path, "audio/mp4", enableRangeProcessing: true)
            : NotFound();

    /// <summary>
    /// Live stream of the track from <paramref name="start"/> seconds while it is converted (no ranges: the player
    /// reloads it from the new position when it seeks outside what it has buffered). Ends with the request.
    /// </summary>
    [HttpGet("{fileId}/audio/{trackId}/stream")]
    public async Task<IActionResult> StreamAudioTrack(Guid fileId, int trackId, [FromQuery] double start = 0)
    {
        if (!double.IsFinite(start) || start < 0) return NotFound();
        if (await FindAudioTrack(fileId, trackId) is not { } source) return NotFound();

        var aborted = HttpContext.RequestAborted;
        Response.ContentType = "audio/aac";
        Response.Headers.CacheControl = "no-store";
        try
        {
            await audioTracks.StreamAsync(source.File, source.Track, TimeSpan.FromSeconds(start), source.InputUrl,
                Response.Body, aborted);
        }
        catch (Exception) when (aborted.IsCancellationRequested)
        {
            // The player went away (seek, track or file change, closed): ffmpeg is gone with it.
        }
        catch (Exception ex) when (Response.HasStarted)
        {
            logger.LogWarning(ex, "Audio stream of {FileId} track {TrackId} broke off", fileId, trackId);
        }
        return new EmptyResult();
    }
    
    /// <summary>
    /// Waveform of a window of the track for the subtitle syncer: <see cref="AudioWaveform.PeaksPerSecond"/> bytes per
    /// second from <paramref name="start"/> (shorter at the end of the file). Only that window is decoded.
    /// </summary>
    [HttpGet("{fileId}/audio/{trackId}/waveform")]
    public async Task<IActionResult> GetAudioWaveform(Guid fileId, int trackId, [FromQuery] double start = 0,
        [FromQuery] double duration = 20)
    {
        if (!double.IsFinite(start) || start < 0 || !double.IsFinite(duration) || duration <= 0) return BadRequest();
        if (await FindAudioTrack(fileId, trackId) is not { } source) return NotFound();
        try
        {
            var peaks = await AudioWaveform.ReadPeaksAsync(source.InputUrl ?? source.File.Path, trackId.ToString(),
                start, duration, HttpContext.RequestAborted);
            Response.Headers.CacheControl = "private, max-age=3600";
            return File(peaks, "application/octet-stream");
        }
        catch (Exception) when (HttpContext.RequestAborted.IsCancellationRequested)
        {
            return new EmptyResult();
        }
    }

    [HttpGet("{fileId}/subtitles/{trackId}")]
    public async Task<ActionResult> GetSubtitleFile(Guid fileId, int trackId)
    {
        if (fileId == Guid.Empty || trackId < 0)
            return NotFound();

        var mediaFile = await mediaFileRepository.GetFileById(fileId);
        if (mediaFile == null) return NotFound();

        if (!System.IO.File.Exists(mediaFile.Path))
            return NotFound();

        // Subtitle files found next to the video are readable while it downloads, its own tracks are not.
        if (trackId < SubtitleTrackDto.ExternalIndexBase && await InProgressDownload(mediaFile) is not null)
            return Conflict("The file is still downloading; tracks can be extracted once it is complete.");

        var filePath = await fileStreamService.GetMediaFileSubtitlesAsync(mediaFile, trackId);
        var extension = Path.GetExtension(filePath).ToLowerInvariant();

        // PGS subtitles are compressed
        if (extension == ".sup")
        {
            return await BuildBrotliFileResponse(filePath);
        }

        return PhysicalFile(
                filePath,
                MimeUtils.GetMimeTypeForFileExtension(extension),
                Path.GetFileName(filePath)
            );
    }    

    private async Task<FileStreamResult> BuildBrotliFileResponse(string filePath)
    {
        var fileStream = System.IO.File.OpenRead(filePath);
        var compressedStream = new MemoryStream();

        await using (var brotli = new BrotliStream(compressedStream, CompressionLevel.Optimal, leaveOpen: true))
        {
            await fileStream.CopyToAsync(brotli);
        }

        compressedStream.Position = 0;

        Response.Headers.ContentEncoding = "br";
        Response.Headers.ContentType = "application/octet-stream"; 
        Response.Headers.ContentDisposition = $"inline; filename=\"{Path.GetFileName(filePath)}.br\"";

        return File(compressedStream, "application/octet-stream");
    }
}