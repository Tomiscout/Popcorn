
namespace VideoPlayer.Shared.PipeEvents;

public class LoadMediaEvent : PipeEvent
{
    public string FilePath { get; set; }
    public string Name { get; set; }
}
