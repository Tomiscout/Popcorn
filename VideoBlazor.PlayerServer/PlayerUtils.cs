using Microsoft.Maui.Controls;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VideoBlazor.PlayerServer
{
    public static class PlayerUtils
    {
        //TODO change this after figuring out how to build everything
        private static readonly string _playerExePath = @"C:\Projects\VideoBlazor\VideoPlayer\bin\Debug\net7.0-windows\VideoPlayer.exe";
        
        public static void LaunchPlayer()
        {
            var path = GetPlayerExePath();
            if(!File.Exists(path))
            {
                throw new FileNotFoundException("VideoPlayer path is not found. " + path);
            }
            Process.Start(path);
        }

        public static string GetPlayerExePath()
        {
            return Path.Combine(FileSystem.Current.AppDataDirectory, _playerExePath);
        }
    }
}
