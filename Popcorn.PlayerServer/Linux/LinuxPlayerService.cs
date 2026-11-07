using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Popcorn.Domain.Interfaces;
using Popcorn.Domain.Models;
using Popcorn.Domain.Services;
using Popcorn.PlayerServer.Linux;
using Popcorn.Shared.Utils;
using PopcornPlayer.Shared.PipeEvents.Client;
using Serilog;

namespace Popcorn.PlayerServer;

public class LinuxPlayerService(
    ILogger<LinuxPlayerService> logger,
    IMediaItemRepository mediaItemRepository,
    MediaItemRefreshService mediaItemRefreshService)
    : IPlayerService
{
    private const string SocketPath = "/tmp/popcorn_bridge.sock";

    private PlayerBridgeServer? _server;

    public async Task<bool> OpenFile(Guid fileId)
    {
        try
        {
            if (_server?.ConnectedSessions > 0)
            {
                logger.LogTrace("Opening file {fileId} and sending it to {sessionCount} socket clients", fileId, _server?.ConnectedSessions);
                _server?.Multicast(JsonSerializer.Serialize(new LoadMediaEvent { FileId = fileId }));
                return true;
            }

            if (_server == null)
            {
                StartPlayerServer();
            }
            LaunchPlayer(fileId.ToString());
            return true;
        } catch(Exception e)
        {
            logger.LogError(e, "Failed to open file with internal player");
            return false;
        }
    }
    public async Task<bool> OpenFile(string filePath)
    {
        try
        {
            if (_server?.ConnectedSessions > 0)
            {
                _server.Multicast(JsonSerializer.Serialize(new LoadMediaEvent { FilePath = filePath }));
                return true;
            }

            if (_server == null)
            {
                StartPlayerServer();
            }
            LaunchPlayer(filePath);
            return true;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to open file with internal player");
            return false;
        }
    }

    private void StartPlayerServer()
    {
        if (File.Exists(SocketPath))
        {
            File.Delete(SocketPath);
        }
        _server = new PlayerBridgeServer(SocketPath);
        _server.Start();
    }

    private void LaunchPlayer(string launchParam)
    {
        var playerPath = GetPlayerElfPath();
        if (string.IsNullOrEmpty(playerPath) || !File.Exists(playerPath))
        {
            throw new FileNotFoundException("Popcorn Player path is not found. " + playerPath);
        }

        var command = $"nohup \"{playerPath}\" \"{launchParam}\" > /dev/null 2>&1 &";
        
        var startInfo = new ProcessStartInfo
        {
            FileName = "/bin/sh",
            Arguments = $"-c \"{command.Replace("\"", "\\\"")}\"", // Pass the command to the shell
            UseShellExecute = true, // Crucial for nohup to work as expected
            CreateNoWindow = true // Prevents a new terminal window from opening
        };
        
        logger.LogTrace("Running player process: {command} {args}", startInfo.FileName, startInfo.Arguments);
        using var process = Process.Start(startInfo); // throws -> OpenFile reports failure
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
            "Popcorn.Player.Linux", "bin", "Debug", "net10.0", "Popcorn.Player.Linux");

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