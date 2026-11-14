namespace Popcorn.Server.PluginHost;

/// <summary>
/// Disposes the plugins when the server stops, for at most <see cref="Budget"/>. A hosted service rather than an
/// <c>ApplicationStopping</c> callback: those run synchronously inside <c>StopApplication</c>, before the host's
/// shutdown timeout applies, so a plugin hanging in Dispose there kept the process alive until systemd killed it.
/// </summary>
public sealed class PluginShutdownService(PluginManager pluginManager, ILogger<PluginShutdownService> logger) : IHostedService
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(5);

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        var unload = pluginManager.UnloadAllAsync(out var pending);
        try
        {
            await unload.WaitAsync(Budget, cancellationToken);
        }
        catch (Exception e) when (e is TimeoutException or OperationCanceledException)
        {
            logger.LogWarning("Plugins still disposing after {s} s, quitting anyway: {ids}", Budget.TotalSeconds, string.Join(", ", pending()));
        }
    }
}
