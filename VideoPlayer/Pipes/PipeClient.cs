using System.Diagnostics;
using VideoPlayer.Shared;
using VideoBlazor.Domain.Models;
using VideoPlayer.Shared.PipeEvents.Client;
using System.Collections;
using System.Collections.Generic;

namespace VideoPlayer.Pipes
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

        public void LoadMedia(LoadMediaEvent loadEvent)
        {
            Debug.WriteLine("Load media received from server " + loadEvent.FilePath);
            PlayerCore.LoadMedia(loadEvent.FilePath);
        }

        public void MediaInfo(MediaItem mediaItem)
        {
            PlayerCore.SetMediaInfo(mediaItem);
        }

        public void UpdateMediaItemHistory(List<ItemHistory> history)
        {
            PlayerCore.UpdateItemHistory(history);
        }
    }
}
