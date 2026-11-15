using Microsoft.AspNetCore.Mvc;
using Popcorn.SegmentFinder;

namespace Popcorn.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SegmentsController(SegmentFinderService segmentFinder) : ControllerBase
{
    /// <summary>Listening clip of an intro/outro candidate or saved reference.</summary>
    [HttpGet("{segmentId:guid}/audio")]
    public IActionResult GetAudio(Guid segmentId)
    {
        var path = SegmentFinderService.GetPreviewPath(segmentId);
        return path is null ? NotFound() : PhysicalFile(path, "audio/ogg", enableRangeProcessing: true);
    }

    /// <summary>
    /// Range editor: waveform peaks (<c>AudioWaveform.PeaksPerSecond</c> bytes per second) of the window
    /// <c>SegmentsEditInfo</c> describes.
    /// </summary>
    [HttpGet("{segmentId:guid}/edit/waveform")]
    public async Task<IActionResult> GetEditWaveform(Guid segmentId, [FromQuery] Guid itemId)
    {
        try
        {
            var peaks = await segmentFinder.ReadEditWaveformAsync(itemId, segmentId, HttpContext.RequestAborted);
            return File(peaks, "application/octet-stream");
        }
        catch (Exception) when (HttpContext.RequestAborted.IsCancellationRequested)
        {
            return new EmptyResult();
        }
        catch (InvalidOperationException e)
        {
            return NotFound(e.Message);
        }
    }

    /// <summary>Range editor: listening clip of the whole window.</summary>
    [HttpGet("{segmentId:guid}/edit/audio")]
    public async Task<IActionResult> GetEditAudio(Guid segmentId, [FromQuery] Guid itemId)
    {
        try
        {
            var path = await segmentFinder.GetEditAudioAsync(itemId, segmentId);
            return PhysicalFile(path, "audio/ogg", enableRangeProcessing: true);
        }
        catch (InvalidOperationException e)
        {
            return NotFound(e.Message);
        }
    }
}
