using Microsoft.AspNetCore.Mvc;
using Popcorn.Domain;
using Popcorn.Server.Logs;

namespace Popcorn.Server.Controllers;

/// <summary>Settings → Logs: every app's log files merged (<see cref="LogReader"/>), polled with a cursor while live.</summary>
[ApiController]
[Route("api/[controller]")]
public class LogsController(IFileService fileService) : ControllerBase
{
    [HttpPost("query")]
    public LogQueryResult Query([FromBody] LogQuery query) => LogReader.Query(query);

    /// <summary>Opens the logs folder in the file manager of the server machine.</summary>
    [HttpPost("open-folder")]
    public IActionResult OpenFolder()
    {
        Directory.CreateDirectory(LogReader.Folder);
        fileService.OpenDirectoryExplorer(LogReader.Folder);
        return NoContent();
    }
}
