using Microsoft.Extensions.Hosting;
using Popcorn.Linux.Tray.Interfaces;

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

    public Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            return _trayManager.HideTray(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return Task.CompletedTask;
        }
    }
}
