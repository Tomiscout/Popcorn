using Microsoft.AspNetCore.Mvc;
using Popcorn.Domain;
using Popcorn.Server.Logs;
using Popcorn.Server.Push;

namespace Popcorn.Server.Controllers;

/// <summary>Settings → Logs: every app's log files merged (<see cref="LogReader"/>); live = <see cref="Live"/>.</summary>
[ApiController]
[Route("api/[controller]")]
public class LogsController(IFileService fileService) : ControllerBase
{
    [HttpPost("query")]
    public LogQueryResult Query([FromBody] LogQuery query) => LogReader.Query(query);

    /// <summary>
    /// What gets appended to the log files after the query's <see cref="LogQuery.Cursor"/> (new files too: the
    /// player's first log, a new day's), as JSON lines (<see cref="JsonLines"/>) of cursor query results while the
    /// viewer stays open. The folder is watched: nothing is sent while no app writes.
    /// </summary>
    [HttpPost("live")]
    public async Task Live([FromBody] LogQuery query)
    {
        var ct = HttpContext.RequestAborted;
        if (query.Cursor is null)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        Directory.CreateDirectory(LogReader.Folder);
        using var changed = new ChangeSignal();
        using var watcher = new FileSystemWatcher(LogReader.Folder)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
        };
        watcher.Changed += (_, _) => changed.Set();
        watcher.Created += (_, _) => changed.Set();
        watcher.Renamed += (_, _) => changed.Set();
        watcher.EnableRaisingEvents = true;

        JsonLines.Begin(Response);
        try
        {
            await Response.Body.FlushAsync(ct);
            changed.Set(); // whatever was written since the client's full query
            while (true)
            {
                await changed.WaitAsync(ct);
                await Task.Delay(LiveBatchDelay, ct); // a burst of lines as one answer
                var result = LogReader.Query(query);
                query.Cursor = result.Cursor;
                if (result.Entries.Count > 0) await JsonLines.WriteAsync(Response, result, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // the viewer closed or left live mode
        }
    }

    private static readonly TimeSpan LiveBatchDelay = TimeSpan.FromMilliseconds(250);

    /// <summary>Opens the logs folder in the file manager of the server machine.</summary>
    [HttpPost("open-folder")]
    public IActionResult OpenFolder()
    {
        Directory.CreateDirectory(LogReader.Folder);
        fileService.OpenDirectoryExplorer(LogReader.Folder);
        return NoContent();
    }
}
