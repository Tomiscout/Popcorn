using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VideoPlayer.Shared.PipeEvents;

namespace VideoPlayer.Shared;

public interface IPlayerPipeClient
{
    public void LoadMedia(LoadMediaEvent loadEvent);
    public void MediaInfo(MediaInfoEvent infoEvent);
}
