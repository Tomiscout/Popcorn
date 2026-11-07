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

namespace Popcorn.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class VideoController(
    IMediaFileRepository mediaFileRepository,
    FileStreamService fileStreamService,
    ItemHistoryService itemHistoryService,
    SegmentSkipService segmentSkipService,
    IMediaItemRepository mediaItemRepository,
    DownloadManager downloads) : ControllerBase
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
    
    [HttpGet("{fileId}/audio/{trackId}")]
    public async Task<ActionResult> GetAudioFile(Guid fileId, int trackId)
    {
        if (fileId == Guid.Empty || trackId < 0)
            return NotFound();

        var mediaFile = await mediaFileRepository.GetFileById(fileId);
        if (mediaFile == null) return NotFound();

        if (!System.IO.File.Exists(mediaFile.Path))
            return NotFound();

        if (await InProgressDownload(mediaFile) is not null)
            return Conflict("The file is still downloading; tracks can be extracted once it is complete.");

        var filePath = await fileStreamService.GetMediaFileAudioTrackAsync(mediaFile, trackId);
        if (filePath == null) return NotFound();

        var extension = Path.GetExtension(filePath).ToLowerInvariant();

        return PhysicalFile(
            filePath,
            MimeUtils.GetMimeTypeForFileExtension(extension),
            Path.GetFileName(filePath)
        );
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

        if (await InProgressDownload(mediaFile) is not null)
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