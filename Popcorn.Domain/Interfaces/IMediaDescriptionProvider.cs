namespace Popcorn.Domain.Interfaces;

/// <summary>Looks up the description of a new library item. The server's implementation asks the media source plugins.</summary>
public interface IMediaDescriptionProvider
{
    /// <param name="name">Item title or release name (may carry a year and release tags).</param>
    /// <returns>Null when nothing was found or the lookup failed; never throws.</returns>
    Task<string?> FindDescriptionAsync(string name);
}
