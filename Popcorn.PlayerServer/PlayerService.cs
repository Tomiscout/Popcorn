using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Reflection;
using Popcorn.Domain.Interfaces;
using Popcorn.Domain.Models;
using Popcorn.Domain.Services;

namespace Popcorn.PlayerServer
{
    public class PlayerService
    {
        private readonly ILogger<PlayerService> _logger;
        private readonly IMediaItemRepository _mediaItemRepository;
        private readonly MediaItemRefreshService _mediaItemRefreshService;

        public PlayerService(ILogger<PlayerService> logger, IMediaItemRepository mediaItemRepository, MediaItemRefreshService mediaItemRefreshService)
        {
            _mediaItemRepository = mediaItemRepository;
            _mediaItemRefreshService = mediaItemRefreshService;
        }

        public async Task OpenFile(string filePath, MediaItem item)
        {
            try
            {
                if (ServerPipeManager.IsConnected())
                {
                    await ServerPipeManager.SendMediaInfoEvent(item);
                    await ServerPipeManager.LoadMediaEvent(filePath);
                    return;
                }

                Task.Run(async () => await ServerPipeManager.StartServer(item, _mediaItemRepository, _mediaItemRefreshService)).ConfigureAwait(false); //TODO is this okay?
                LaunchPlayer(filePath);
            } catch(Exception e)
            {
                _logger.LogError(e, "Failed to open file with internal player");
            }
        }

        private void LaunchPlayer(string mediaPath)
        {
            var path = GetPlayerExePath();
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("PopcornPlayer path is not found. " + path);
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = path,
                Arguments = $"\"{mediaPath}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,  // Redirect the standard output
                RedirectStandardError = true,  // Redirect the error output
                Environment =
                {
                    { "LANG", "en_US.UTF-8" }
                }
            };
            if(Environment.GetEnvironmentVariable("UseLocalWeb")?.ToLower() == "true") // Inject develop web player
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

            if (Environment.GetEnvironmentVariable("PlayerDebugger")?.ToLower() == "true") // Inject develop web player
            {
                DebugUtils.AttachProcessToDebugger(process.Id);
            }
        }

        private string GetPlayerExePath()
        {
            var binLocation = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            var installedExe = Path.Join(binLocation, @"\Player\PopcornPlayer.exe");
            if(File.Exists(installedExe))
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
            if(binLocation == null)
            {
                throw new Exception("Popcorn folder not found");
            }
            binLocation = Path.GetDirectoryName(binLocation);


            if(Debugger.IsAttached)
            {
                return Path.Join(binLocation, @"\PopcornPlayer\bin\Debug\net7.0-windows\PopcornPlayer.exe");
            }
            return Path.Join(binLocation, @"\PopcornPlayer\bin\Release\net7.0-windows\PopcornPlayer.exe");
        }
    }
}
