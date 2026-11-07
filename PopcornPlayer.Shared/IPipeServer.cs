
using PopcornPlayer.Shared.PipeEvents.Server;

namespace PopcornPlayer.Shared;

public interface IPipeServer
{
    public Task OnClientInitialized();

    public Task TrackPlayed(TrackPlayedEvent e);
}