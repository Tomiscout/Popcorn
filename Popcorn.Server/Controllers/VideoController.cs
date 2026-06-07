using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Popcorn.Application.Services;
using Popcorn.Domain.Interfaces;
using Popcorn.Domain.Utils;
using Popcorn.Server.Controllers.Models;
using Popcorn.Server.Services;

namespace Popcorn.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class VideoController(IMediaFileRepository mediaFileRepository, FileStreamService fileStreamService, ItemHistoryService itemHistoryService) : ControllerBase
{
    [HttpGet("{fileId}")]
    public async Task<IActionResult> Get(Guid fileId)
    {
        if(fileId == Guid.Empty) return NotFound();
        
        var mediaFile = await mediaFileRepository.GetFileById(fileId);
        if(mediaFile == null) return NotFound();
        
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
        
        if (!System.IO.File.Exists(mediaFile.Path))
            return NotFound();

        return Ok(await fileStreamService.GetMediaFileMetadataAsync(mediaFile));
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

        var filePath = await fileStreamService.GetMediaFileAudioTrackAsync(mediaFile, trackId);
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