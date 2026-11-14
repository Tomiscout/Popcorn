using Microsoft.AspNetCore.Mvc;
using Popcorn.Application.Services;

namespace Popcorn.Server.Controllers;

[ApiController]
[Route("api/history")]
public class HistoryController(ItemHistoryService itemHistory) : ControllerBase
{
    /// <summary>
    /// The native player wrote the item's history to the database itself (it works without the server): pages
    /// showing the item get a <c>MediaItemHistoryChanged</c> ping and refetch it.
    /// </summary>
    [HttpPost("{itemId:guid}/changed")]
    public IActionResult Changed(Guid itemId)
    {
        itemHistory.NotifyChanged(itemId);
        return NoContent();
    }
}
