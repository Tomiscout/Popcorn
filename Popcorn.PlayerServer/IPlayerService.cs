using Popcorn.Domain.Models;

namespace Popcorn.PlayerServer;

public interface IPlayerService
{
    Task OpenFile(Guid fileId);
    Task OpenFileWindows(string filePath, MediaItem item); //TODO remove

    void Dispose(); //TODO call this from root program
}