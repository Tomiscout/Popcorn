using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VideoBlazor.Domain.Models;

public class PlaybackHistory
{
    public string Filename { get; set; }
    public string Title { get; set; }
    public DateTime PlaybackTime { get; set; }
}
