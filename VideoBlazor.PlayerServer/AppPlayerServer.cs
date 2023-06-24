using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VideoBlazor.Domain.Models;
using VideoPlayer.Shared;

namespace VideoBlazor.PlayerServer
{
    public class PipeServer : IPipeServer
    {
        private MediaItem _initialItem;
        
        public PipeServer(MediaItem initialItem)
        {
            _initialItem = initialItem;
        }

        public async Task OnClientInitialized() {
            // Hacky way to fix sync problems at start. This wont be a problem at this point
            // TODO maybe this hack is unnecessary, i was testing on broken file
            //await ServerPipeManager.SendMediaInfoEvent(_initialItem);
            _initialItem = null;
        }
    }
}
