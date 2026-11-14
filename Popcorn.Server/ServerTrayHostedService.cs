using Microsoft.Extensions.Hosting;
using Popcorn.Shared.Interfaces;

namespace Popcorn.Server;

internal sealed class ServerTrayHostedService : IHostedService
{
    private readonly ITrayManager _trayManager;

    public ServerTrayHostedService(ITrayManager trayManager)
    {
        _trayManager = trayManager;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!await _trayManager.CanUseTray(cancellationToken))
        {
            return;
        }

        await _trayManager.ShowTray(cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _trayManager.HideTray(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // The host gave up on stopping; the icon goes away with the process.
        }
    }
}
