using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VideoPlayer.Pipes.PipeEvents
{
    public class LoadedMediaEvent : PipeEvent
    {
        public string FilePath { get; set; }
        public string Name { get; set; }
    }
}
