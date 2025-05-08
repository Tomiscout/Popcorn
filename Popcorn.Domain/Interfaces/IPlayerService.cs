namespace Popcorn.Domain.Interfaces;

public interface IPlayerService
{
    Task OpenFile(Guid fileId);
    Task OpenFile(string filePath);

    void Dispose(); //TODO call this from root program
}