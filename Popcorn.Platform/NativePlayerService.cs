using Microsoft.Extensions.Logging;
using Popcorn.Domain.Interfaces;
using Popcorn.Shared;

namespace Popcorn.Platform;

/// <summary>
/// Opens files in the native Popcorn player: hands them to a running one over <see cref="PlayerLink"/>, else starts
/// one with the file as its argument. Subclasses only know how to start the player of their OS.
/// </summary>
public abstract class NativePlayerService(ILogger logger) : IPlayerService
{
    /// <summary>How long a player we started may take to start listening before we start another one.</summary>
    private static readonly TimeSpan StartupTime = TimeSpan.FromSeconds(15);

    private readonly SemaphoreSlim _lock = new(1, 1);
    private DateTime _launchedAt = DateTime.MinValue;

    public Task<bool> OpenFile(Guid fileId) => OpenAsync(new PlayerLinkMessage(FileId: fileId), fileId.ToString());

    public Task<bool> OpenFile(string filePath) => OpenAsync(new PlayerLinkMessage(FilePath: filePath), filePath);

    private async Task<bool> OpenAsync(PlayerLinkMessage message, string argument)
    {
        await _lock.WaitAsync();
        try
        {
            // The player listens once it is ready, so one we have just started gets the file when it is.
            var deadline = _launchedAt + StartupTime;
            while (true)
            {
                if (await PlayerLink.TrySendAsync(message))
                {
                    logger.LogInformation("Sent {argument} to the running player", argument);
                    return true;
                }
                if (DateTime.UtcNow >= deadline) break;
                await Task.Delay(250);
            }

            logger.LogInformation("Starting the player with {argument}", argument);
            LaunchPlayer(argument);
            _launchedAt = DateTime.UtcNow;
            return true;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to open {argument} with the Popcorn player", argument);
            return false;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Starts the player process with <paramref name="argument"/> (file id or path); throws when it can't.</summary>
    protected abstract void LaunchPlayer(string argument);

    /// <summary>The player next to the server (bundle), else its build output in the repo (dev: server in Popcorn.Server/bin/&lt;config&gt;/net10.0).</summary>
    protected static string FindPlayer(string bundledPath, string projectName, string executableName)
    {
        var bundled = Path.Combine(AppContext.BaseDirectory, bundledPath);
        if (File.Exists(bundled)) return bundled;

        var repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        foreach (var configuration in new[] { "Debug", "Release" })
        {
            var dev = Path.Combine(repo, projectName, "bin", configuration, "net10.0", executableName);
            if (File.Exists(dev)) return dev;
        }
        throw new FileNotFoundException($"Popcorn player not found ({bundled})");
    }
}
