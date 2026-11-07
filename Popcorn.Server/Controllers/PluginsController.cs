using Microsoft.AspNetCore.Mvc;
using Popcorn.Server.PluginHost;

namespace Popcorn.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PluginsController(PluginManager pluginManager, ILogger<PluginsController> logger) : ControllerBase
{
    /// <summary>Installs (or replaces) a plugin from a zip; multipart field <c>file</c>. Returns the plugin id.</summary>
    [HttpPost]
    [RequestSizeLimit(512L * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 512L * 1024 * 1024)]
    public IActionResult Install(IFormFile file)
    {
        try
        {
            using var stream = file.OpenReadStream();
            return Ok(new { id = pluginManager.Install(stream) });
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Plugin install from {file} failed", file.FileName);
            return BadRequest(ex.Message);
        }
    }
}
