namespace PopcornPlayer.Shared.PipeEvents.Client;

public class LoadMediaEventWindows : PipeEvent
{
    public LoadMediaEventWindows()
    {
        EventName = nameof(LoadMediaEvent);
    }
    public string FilePath { get; set; }
}
