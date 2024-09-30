using Popcorn.Domain.Models;

namespace Popcorn.PlayerServer;

public interface IPlayerService
{
    Task OpenFile(string filePath, MediaItem item);

    void Dispose(); //TODO call this from root program
}