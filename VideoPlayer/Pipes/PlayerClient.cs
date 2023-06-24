using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VideoPlayer.Shared;
using VideoPlayer.Shared.PipeEvents;
using WebSiteRatings;

namespace VideoPlayer.Pipes
{
    internal class PlayerPipeClient : IPlayerPipeClient
    {
        private MainWindow _mainWindow;
        
        public PlayerPipeClient(MainWindow mainWindow)
        {
            _mainWindow = mainWindow;
        }

        public void LoadMedia(LoadMediaEvent loadEvent)
        {
            Debug.WriteLine("Load media received from server " + loadEvent.FilePath);
            _mainWindow.LoadMedia(loadEvent.FilePath);
        }

        public void MediaInfo(MediaInfoEvent infoEvent)
        {
            _mainWindow.SetMediaInfo(infoEvent);
        }
    }
}
