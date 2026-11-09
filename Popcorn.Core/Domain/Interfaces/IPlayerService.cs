namespace Popcorn.Domain.Interfaces;

/// <summary>The native Popcorn player (Popcorn.Platform: <c>NativePlayerService</c>).</summary>
public interface IPlayerService
{
    /// <returns>False when the player could not be launched (e.g. not installed).</returns>
    Task<bool> OpenFile(Guid fileId);
    Task<bool> OpenFile(string filePath);
}
