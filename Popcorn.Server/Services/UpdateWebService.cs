using Popcorn.Server.Attributes;
using Popcorn.Server.Models;
using Popcorn.Server.Models.Requests;

namespace Popcorn.Server.Services;

/// <summary>Settings page → Updates: check silently, then (on the user's yes) run the installer's update.</summary>
[WebSocketService]
public class UpdateWebService(WebSocketBus bus, UpdateService updates, ILogger<UpdateWebService> logger)
{
    [WebSocketEvent(SocketRequests.UpdateStatus)]
    public async Task SendStatus()
    {
        await bus.SendEventAsync(SocketRequests.UpdateStatus, new UpdateStatus
        {
            Supported = updates.IsSupported,
            CurrentVersion = updates.CurrentVersion(),
        });
    }

    [WebSocketEvent(SocketRequests.UpdateCheck)]
    public Task Check()
    {
        // Seconds of GitHub: not on the socket's message loop, the answer comes as its own event.
        _ = Task.Run(async () =>
        {
            UpdateCheckResult result;
            try
            {
                result = await updates.CheckAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Update check failed");
                result = new UpdateCheckResult { Status = UpdateCheckStatus.Failed, Error = ex.Message };
            }
            try
            {
                await bus.SendEventAsync(SocketRequests.UpdateCheck, result);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Update check answer not delivered (page closed?)");
            }
        });
        return Task.CompletedTask;
    }

    [WebSocketEvent(SocketRequests.UpdateInstall)]
    public Task Install()
    {
        updates.StartUpdate();
        return Task.CompletedTask;
    }
}
