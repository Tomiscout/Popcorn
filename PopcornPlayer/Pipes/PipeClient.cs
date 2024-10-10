using System.Diagnostics;
using PopcornPlayer.Shared;
using Popcorn.Domain.Models;
using PopcornPlayer.Shared.PipeEvents.Client;
using System.Collections;
using System.Collections.Generic;
using PopcornPlayer.Core;

namespace PopcornPlayer.Pipes
{
    internal class PipeClient : IPipeClient
    {
        private MainWindow _mainWindow;
        
        public PipeClient(MainWindow mainWindow)
        {
            _mainWindow = mainWindow;
        }

        //TODO dont work
        public void Kill()
        {
            PipeManager.Dispose();
            _mainWindow.Close();
        }

        public void LoadMedia(LoadMediaEventWindows loadEvent)
        {
            Debug.WriteLine("Load media received from server " + loadEvent.FilePath);
            PlayerCore.LoadMedia(loadEvent.FilePath);
        }

        public void MediaInfo(MediaItem mediaItem)
        {
            MediaItemManager.SetMediaInfo(mediaItem);
        }

        public void UpdateMediaItemHistory(List<ItemHistory> history)
        {
            MediaItemManager.UpdateItemHistory(history);
        }
    }
}
