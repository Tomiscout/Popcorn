namespace Popcorn.Domain.Interfaces;

public interface IPlayerService
{
    /// <returns>False when the player could not be launched (e.g. not installed).</returns>
    Task<bool> OpenFile(Guid fileId);
    Task<bool> OpenFile(string filePath);

    void Dispose(); //TODO call this from root program
}