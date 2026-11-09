using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Popcorn.Domain.Interfaces;
using Popcorn.Downloads;
using Popcorn.Server.Push;

namespace Popcorn.Server.Controllers;

/// <summary>Files that are still downloading, for the players (browser, native players via HTTP).</summary>
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

    /// <summary>
    /// <see cref="Playback"/> as JSON lines (<see cref="JsonLines"/>): the state now, then again whenever it changes,
    /// until the file is complete or the player goes away. 404 when it is not one of ours.
    /// </summary>
    [HttpGet("playback/live")]
    public Task PlaybackLive([FromQuery] string path) => StreamPlaybackAsync(path);

    /// <summary>Same as <see cref="PlaybackLive"/> for a library file.</summary>
    [HttpGet("playback/{fileId:guid}/live")]
    public async Task PlaybackOfFileLive(Guid fileId)
    {
        var file = await mediaFileRepository.GetFileById(fileId);
        await StreamPlaybackAsync(file?.Path);
    }

    private async Task StreamPlaybackAsync(string? path)
    {
        var ct = HttpContext.RequestAborted;
        var playback = path is null ? null : await downloads.GetPlaybackAsync(path);
        if (playback is null)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        using var changed = new ChangeSignal();
        downloads.Changed += changed.Set;
        try
        {
            JsonLines.Begin(Response);
            IReadOnlyList<double[]>? sentRanges = null;
            while (playback is not null)
            {
                if (sentRanges is null || !SameRanges(sentRanges, playback.Ranges) || playback.Complete)
                {
                    await JsonLines.WriteAsync(Response, playback, ct);
                    sentRanges = playback.Ranges;
                }
                if (playback.Complete) return;
                await changed.WaitAsync(ct);
                playback = await downloads.GetPlaybackAsync(path!);
            }
        }
        catch (OperationCanceledException)
        {
            // the player left
        }
        finally
        {
            downloads.Changed -= changed.Set;
        }
    }

    private static bool SameRanges(IReadOnlyList<double[]> a, IReadOnlyList<double[]> b) =>
        a.Count == b.Count && a.Zip(b).All(p => p.First.AsSpan().SequenceEqual(p.Second));
}
