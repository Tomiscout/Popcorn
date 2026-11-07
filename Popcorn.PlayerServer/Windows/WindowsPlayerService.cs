using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Popcorn.Domain.Interfaces;
using Popcorn.Domain.Models;
using Popcorn.Domain.Services;

namespace Popcorn.PlayerServer.Windows;

public class WindowsPlayerService : IPlayerService
{
    private readonly ILogger<WindowsPlayerService> _logger;
    private readonly IMediaItemRepository _mediaItemRepository;
    private readonly MediaItemRefreshService _mediaItemRefreshService;

    public WindowsPlayerService(ILogger<WindowsPlayerService> logger, IMediaItemRepository mediaItemRepository, MediaItemRefreshService mediaItemRefreshService)
    {
        _logger = logger;
        _mediaItemRepository = mediaItemRepository;
        _mediaItemRefreshService = mediaItemRefreshService;
    }

    public async Task<bool> OpenFile(Guid fileId)
    {
        try
        {
            if (ServerPipeManager.IsConnected())
            {
                await ServerPipeManager.LoadMediaEvent(fileId);
                return true;
            }

            Task.Run(async () => await ServerPipeManager.StartServer(_mediaItemRepository, _mediaItemRefreshService)).ConfigureAwait(false); //TODO is this okay?
            LaunchPlayer(fileId.ToString());
            return true;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to open file with internal player");
            return false;
        }
    }

    public async Task<bool> OpenFile(string filePath)
    {
        try
        {
            if (ServerPipeManager.IsConnected())
            {
                await ServerPipeManager.LoadMediaEventPath(filePath);
                return true;
            }

            Task.Run(async () => await ServerPipeManager.StartServer(_mediaItemRepository, _mediaItemRefreshService)).ConfigureAwait(false); //TODO is this okay?
            LaunchPlayer(filePath);
            return true;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to open file with internal player");
            return false;
        }
    }

    private void LaunchPlayer(string launchParam)
    {
        var path = GetPlayerExePath();
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("PopcornPlayer path is not found. " + path);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = path,
            Arguments = $"\"{launchParam}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,  // Redirect the standard output
            RedirectStandardError = true,  // Redirect the error output
            Environment =
                {
                    { "LANG", "en_US.UTF-8" }
                }
        };
        if (Environment.GetEnvironmentVariable("UseLocalWeb")?.ToLower() == "true") // Inject develop web player
        {
            startInfo.Environment.Add("UseLocalWeb", "true");
        }

        var process = new Process() { StartInfo = startInfo };
        // Handle standard output
        process.OutputDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                Debug.WriteLine($"[PopcornPlayer]: {e.Data}");
            }
        };

        // Handle error output
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

        /* if (Environment.GetEnvironmentVariable("PlayerDebugger")?.ToLower() == "true") // Inject develop web player
         {
             DebugUtils.AttachProcessToDebugger(process.Id);
         }*/
    }

    private string GetPlayerExePath()
    {
        var binLocation = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        var installedExe = Path.Join(binLocation, @"\Player\Popcorn.Player.Windows.exe");
        if (File.Exists(installedExe))
        {
            return installedExe;
        }

        //Find path for development environment
        while (Path.GetFileName(binLocation) != "Popcorn" && binLocation != Path.GetPathRoot(binLocation))
        {
            binLocation = Path.GetDirectoryName(binLocation);

            if (string.IsNullOrEmpty(binLocation))
            {
                throw new Exception("Popcorn folder not found");
            }
        }
        if (binLocation == null)
        {
            throw new Exception("Popcorn folder not found");
        }
        binLocation = Path.GetDirectoryName(binLocation);

        if (Debugger.IsAttached)
        {
            return Path.Join(binLocation, @"\Popcorn.Player.Windows\bin\Debug\net10.0\Popcorn.Player.Windows.exe");
        }
        return Path.Join(binLocation, @"\Popcorn.Player.Windows\bin\Release\net10.0\Popcorn.Player.Windows.exe");
    }

    public void Dispose()
    {
        ServerPipeManager.Dispose();
    }
}