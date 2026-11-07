using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Popcorn.Domain.Interfaces;
using Popcorn.Downloads;

namespace Popcorn.Server.Controllers;

/// <summary>Files that are still downloading, for the players (browser, Linux player via HTTP).</summary>
[ApiController]
[Route("api/downloads")]
public class DownloadsController(DownloadManager downloads, IMediaFileRepository mediaFileRepository) : ControllerBase
{
    private static readonly FileExtensionContentTypeProvider ContentTypes = new()
    {
        // Browsers play Matroska with web codecs when it is announced as WebM.
        Mappings = { [".mkv"] = "video/webm" },
    };

    /// <summary>
    /// The file as a seekable stream: range requests for parts that are not downloaded yet move the download there and
    /// wait for the data.
    /// </summary>
    [HttpGet("{id:guid}/files/{index:int}")]
    public async Task<IActionResult> Stream(Guid id, int index)
    {
        if (await downloads.OpenStream(id, index) is not var (stream, fileName)) return NotFound();
        if (!ContentTypes.TryGetContentType(fileName, out var contentType)) contentType = "application/octet-stream";
        return File(stream, contentType, enableRangeProcessing: true);
    }

    /// <summary>Download state of a file on disk (any path the player opened); 404 when it is not one of ours.</summary>
    [HttpGet("playback")]
    public async Task<ActionResult<DownloadPlaybackDto>> Playback([FromQuery] string path) =>
        await downloads.GetPlaybackAsync(path) is { } playback ? playback : NotFound();

    /// <summary>Same as <see cref="Playback"/> for a library file.</summary>
    [HttpGet("playback/{fileId:guid}")]
    public async Task<ActionResult<DownloadPlaybackDto>> PlaybackOfFile(Guid fileId)
    {
        var file = await mediaFileRepository.GetFileById(fileId);
        return file is not null && await downloads.GetPlaybackAsync(file.Path) is { } playback ? playback : NotFound();
    }
}
