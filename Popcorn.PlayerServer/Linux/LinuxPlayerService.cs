using System.Diagnostics;
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
                _server.SendEvent(new LoadMediaEvent { FileId = fileId });
                return;
            }

            if (_server == null)
            {
                StartPlayerServer();
                LaunchPlayer(fileId);
            }
            else
            {
                _logger.LogWarning("Socket server is running, but it has no clients yet. Ignoring file id: {fileId}", fileId);
            }
        } catch(Exception e)
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

    private void LaunchPlayer(Guid fileId)
    {
        var path = GetPlayerElfPath();
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            throw new FileNotFoundException("Popcorn Player path is not found. " + path);
        }
        
        var startInfo = new ProcessStartInfo
        {
            FileName = path,
            Arguments = $"{fileId.ToString()}",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        
        var process = new Process() { StartInfo = startInfo };
        // Handle standard output
        process.OutputDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                Debug.WriteLine($"[PopcornPlayer]: {e.Data}");
            }
        };
        process.ErrorDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                Debug.WriteLine($"[PopcornPlayer-ERROR]: {e.Data}");
            }
        };
        
        process.Start();
        process.BeginOutputReadLine();  // Start async read of standard output
        process.BeginErrorReadLine();  // Start async read of error output
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
}