using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VideoPlayer.Shared;

namespace VideoPlayer.Pipes
{
    internal class PlayerPipeClient : IPlayerPipeClient
    {
        public void LoadMedia(string path)
        {
            Debug.WriteLine("Load media received from server " + path);
        }
    }
}
