using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Popcorn.Domain.Models;

namespace PopcornPlayer.Shared.PipeEvents.Server;
public class TrackPlayedEvent : PipeEvent
{
    public TrackPlayedEvent()
    {
        EventName = nameof(TrackPlayedEvent);
    }
    public Guid ItemId { get; set; }
    public ItemHistory HistoryItem { get; set; }
}
