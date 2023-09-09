namespace VideoPlayer.Shared.PipeEvents.Client;

public class LoadMediaEvent : PipeEvent
{
    public string FilePath { get; set; }
    public string Name { get; set; }
}
