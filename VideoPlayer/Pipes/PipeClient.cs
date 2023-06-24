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
            _mainWindow.LoadMedia(loadEvent.FilePath);
        }

        public void MediaInfo(MediaInfoEvent infoEvent)
        {
            _mainWindow.SetMediaInfo(infoEvent);
        }
    }
}
