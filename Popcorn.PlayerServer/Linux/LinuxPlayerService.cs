using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Popcorn.Domain.Interfaces;
using Popcorn.Domain.Models;
using Popcorn.Domain.Services;
using Popcorn.PlayerServer.Linux;
using PopcornPlayer.Shared.PipeEvents.Client;

namespace Popcorn.PlayerServer;

public class LinuxPlayerService : IPlayerService
{
    private const string _socketPath = "/tmp/popcorn_bridge.sock";

    private readonly ILogger<LinuxPlayerService> _logger;
    private readonly IMediaItemRepository _mediaItemRepository;
    private readonly MediaItemRefreshService _mediaItemRefreshService;
    private PlayerBridgeServer? _server;

    public LinuxPlayerService(ILogger<LinuxPlayerService> logger, IMediaItemRepository mediaItemRepository, MediaItemRefreshService mediaItemRefreshService)
    {
        _logger = logger;
        _mediaItemRepository = mediaItemRepository;
        _mediaItemRefreshService = mediaItemRefreshService;
    }
    

    public async Task OpenFile(Guid fileId)
    {
        try
        {
            if (_server?.ConnectedSessions > 0)
            {
                _server.Multicast(JsonSerializer.Serialize(new LoadMediaEvent { FileId = fileId }));
                return;
            }

            if (_server == null)
            {
                StartPlayerServer();
            }
            LaunchPlayer(fileId.ToString());
        } catch(Exception e)
        {
            _logger.LogError(e, "Failed to open file with internal player");
        }
    }
    public async Task OpenFile(string filePath)
    {
        try
        {
            if (_server?.ConnectedSessions > 0)
            {
                _server.Multicast(JsonSerializer.Serialize(new LoadMediaEvent { FilePath = filePath }));
                return;
            }

            if (_server == null)
            {
                StartPlayerServer();
            }
            LaunchPlayer(filePath);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to open file with internal player");
        }
    }

    private void StartPlayerServer()
    {
        if (File.Exists(_socketPath))
        {
            File.Delete(_socketPath);
        }
        _server = new PlayerBridgeServer(_socketPath);
        _server.Start();
    }

    private void LaunchPlayer(string launchParam)
    {
        var playerPath = GetPlayerElfPath();
        if (string.IsNullOrEmpty(playerPath) || !File.Exists(playerPath))
        {
            throw new FileNotFoundException("Popcorn Player path is not found. " + playerPath);
        }

        // Use nohup to detach the process and redirect output
        // The '&' at the end runs the command in the background
        var command = $"nohup \"{playerPath}\" \"{launchParam}\" > /dev/null 2>&1 &";

        var startInfo = new ProcessStartInfo
        {
            FileName = "/bin/bash", // Or "/bin/sh"
            Arguments = $"-c \"{command.Replace("\"", "\\\"")}\"", // Pass the command to the shell
            UseShellExecute = true, // Crucial for nohup to work as expected
            CreateNoWindow = true // Prevents a new terminal window from opening
        };

        try
        {
            using (var process = Process.Start(startInfo))
            {
                // We don't wait for exit or redirect output/error here
                // because nohup handles it.
                // The 'process' object here represents the shell process (bash/sh),
                // not the player process itself.
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to launch player as detached process");
        }
    }

    private string GetPlayerElfPath()
    {
        var localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Popcorn.Player.Linux");
        if (File.Exists(localPath))
        {
            return localPath;
        }
        
        //In development, player bundle is in different folder
        var debugPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, 
            "..", "..", "..", "..", 
            "Popcorn.Player.Linux", "bin", "Debug", "net8.0", "Popcorn.Player.Linux");

        return File.Exists(debugPath) ? debugPath : string.Empty;
    }
    
    public void Dispose()
    {
        _server?.DisconnectAll();
        _server?.Dispose();
    }

    public Task OpenFileWindows(string filePath, MediaItem item)
    {
        throw new NotImplementedException();
    }
}