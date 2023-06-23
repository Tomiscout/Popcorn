using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VideoPlayer.Shared;

public interface IPlayerPipeClient
{
    public void LoadMedia(string path);
}
