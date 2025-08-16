using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Popcorn.Domain.Interfaces;
using Popcorn.Server.Controllers.Models;
using Popcorn.Server.Services;

namespace Popcorn.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class VideoController(IMediaFileRepository mediaFileRepository, FileStreamService fileStreamService) : ControllerBase
{
    [HttpGet("{fileId}")]
    public async Task<IActionResult> Get(Guid fileId)
    {
        if(fileId == Guid.Empty) return NotFound();
        
        var mediaFile = await mediaFileRepository.GetFileById(fileId);
        if(mediaFile == null) return NotFound();
        
        if (!System.IO.File.Exists(mediaFile.Path))
            return NotFound();
        
        var streamFile = await fileStreamService.PrepareStreamFileAsync(mediaFile.Path, mediaFile.Id);
        if (streamFile != null)
        {
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
}