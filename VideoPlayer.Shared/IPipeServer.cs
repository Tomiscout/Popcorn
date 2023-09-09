
using VideoPlayer.Shared.PipeEvents.Server;

namespace VideoPlayer.Shared;

public interface IPipeServer
{
    public Task OnClientInitialized();

    public Task TrackPlayed(TrackPlayedEvent e);
}