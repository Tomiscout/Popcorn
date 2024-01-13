using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VideoBlazor.Domain.Models;

namespace VideoPlayer.Shared.PipeEvents.Server;
public class TrackPlayedEvent : PipeEvent
{
    public TrackPlayedEvent()
    {
        EventName = nameof(TrackPlayedEvent);
    }
    public Guid ItemId { get; set; }
    public ItemHistory HistoryItem { get; set; }
}
