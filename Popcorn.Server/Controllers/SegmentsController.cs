using Microsoft.AspNetCore.Mvc;
using Popcorn.SegmentFinder;

namespace Popcorn.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SegmentsController : ControllerBase
{
    /// <summary>Listening clip of an intro/outro candidate or saved reference.</summary>
    [HttpGet("{segmentId:guid}/audio")]
    public IActionResult GetAudio(Guid segmentId)
    {
        var path = SegmentFinderService.GetPreviewPath(segmentId);
        return path is null ? NotFound() : PhysicalFile(path, "audio/ogg", enableRangeProcessing: true);
    }
}
