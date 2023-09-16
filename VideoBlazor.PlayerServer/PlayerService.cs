using System.Diagnostics;
using VideoBlazor.Domain.Interfaces;
using VideoBlazor.Domain.Models;

namespace VideoBlazor.PlayerServer
{
    public class PlayerService
    {
        private readonly IMediaItemRepository _mediaItemRepository;

        public PlayerService(IMediaItemRepository mediaItemRepository)
        {
            _mediaItemRepository = mediaItemRepository;
        }

        //TODO change this after figuring out how to build everything
        private const string _playerExePath = @"C:\Projects\VideoBlazor\VideoPlayer\bin\Debug\net7.0-windows\VideoPlayer.exe";

        public async Task OpenFile(string filePath, MediaItem item)
        {
            if (ServerPipeManager.IsConnected())
            {
                await ServerPipeManager.LoadMediaEvent(filePath);
                await ServerPipeManager.SendMediaInfoEvent(item);
                return;
            }

            Task.Run(async () => await ServerPipeManager.StartServer(item, _mediaItemRepository)).ConfigureAwait(false); //TODO is this okay?
            LaunchPlayer(filePath);
        }

        private void LaunchPlayer(string mediaPath)
        {
            var path = GetPlayerExePath();
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("VideoPlayer path is not found. " + path);
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = path,
                Arguments = mediaPath,
                UseShellExecute = false,
                Environment =
                {
                    { "LANG", "en_US.UTF-8" }
                }
            };

            Process.Start(startInfo);
        }

        private string GetPlayerExePath()
        {
            return Path.Combine(FileSystem.Current.AppDataDirectory, _playerExePath);
        }
    }
}
