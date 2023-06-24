using System.Diagnostics;
using VideoBlazor.Domain.Models;

namespace VideoBlazor.PlayerServer
{
    public static class PlayerUtils
    {
        //TODO change this after figuring out how to build everything
        private static readonly string _playerExePath = @"C:\Projects\VideoBlazor\VideoPlayer\bin\Debug\net7.0-windows\VideoPlayer.exe";
        
        public static async Task OpenFile(string filePath, MediaItem item)
        {
            if (ServerPipeManager.IsConnected())
            {
                await ServerPipeManager.LoadMediaEvent(filePath);
                await ServerPipeManager.SendMediaInfoEvent(item);
            }
            else
            {
                Task.Run(async () => await ServerPipeManager.StartServer(item)).ConfigureAwait(false);
                LaunchPlayer(filePath);
            }
        }

        public static void LaunchPlayer(string mediaPath)
        {
            var path = GetPlayerExePath();
            if(!File.Exists(path))
            {
                throw new FileNotFoundException("VideoPlayer path is not found. " + path);
            }
            var arguments = new List<string>() { mediaPath };
            Process.Start(path, arguments);
        }

        public static string GetPlayerExePath()
        {
            return Path.Combine(FileSystem.Current.AppDataDirectory, _playerExePath);
        }
    }
}
