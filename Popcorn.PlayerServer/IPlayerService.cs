using Popcorn.Domain.Models;

namespace Popcorn.PlayerServer;

public interface IPlayerService
{
    Task OpenFile(Guid fileId);

    void Dispose(); //TODO call this from root program
}