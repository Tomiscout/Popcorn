namespace PopcornPlayer.Shared.PipeEvents.Client;

public class LoadMediaEvent : PipeEvent
{
    public LoadMediaEvent()
    {
        EventName = nameof(LoadMediaEvent);
    }
    public Guid? FileId { get; set; }

    public string? FilePath { get; set; }
}
