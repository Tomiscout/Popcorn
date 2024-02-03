using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Popcorn.Domain.Models;

public class SettingsModel
{
    public bool UseInternalPlayer { get; set; }
    public bool UseInternalTorrentClient { get; set; }
}
